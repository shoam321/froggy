using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace BluetoothWidget
{
    public class WindowsBluetoothDevice
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public int? BatteryLevel { get; set; }
        public bool IsBatteryFallback { get; set; } // True if battery came from less reliable source (e.g., HFP AG)
    }

    /// <summary>
    /// Holds battery info with reliability indicator
    /// </summary>
    internal struct BatteryInfo
    {
        public int Level;
        public bool IsFallback;
    }

    // CM_* / SetupAPI interop for querying DEVPKEY_Bluetooth_Battery
    internal static class CfgMgr32
    {
        public const uint CR_SUCCESS = 0;
        public const uint CM_LOCATE_DEVNODE_NORMAL = 0;
        public const uint DEVPROP_TYPE_BYTE = 0x00000003;

        // DEVPKEY_Bluetooth_Battery: {104ea319-6ee2-4701-bd47-8ddbf425bbe5}, 2
        public static readonly Guid DEVPKEY_Bluetooth_Battery_fmtid = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5");
        public const uint DEVPKEY_Bluetooth_Battery_pid = 2;

        // DEVPKEY_Device_FriendlyName: {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
        public static readonly Guid DEVPKEY_Device_FriendlyName_fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        public const uint DEVPKEY_Device_FriendlyName_pid = 14;

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint CM_Get_Device_ID_List_Size(out uint pulLen, string? pszFilter, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint CM_Get_Device_ID_ListW(string? pszFilter, char[] Buffer, uint BufferLen, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint CM_Get_DevNode_PropertyW(
            uint dnDevInst,
            ref DEVPROPKEY PropertyKey,
            out uint PropertyType,
            IntPtr PropertyBuffer,
            ref uint PropertyBufferSize,
            uint ulFlags);
    }

    public class WindowsBluetoothHelper
    {
        private static readonly string[] BatteryPropertyKeys =
        {
            // Commonly returned for BLE devices
            "System.Devices.Aep.Bluetooth.Le.BatteryLevel",

            // Sometimes present for classic or composite devices (varies by driver/device)
            "System.Devices.Aep.Bluetooth.BatteryLevel",
        };

        private static readonly string[] AepConnectedPropertyKeys =
        {
            "System.Devices.Aep.IsConnected",
            "System.Devices.Connected",
        };

        private static void Log(string message)
        {
            try
            {
                App.LogToFile("Info", new Exception(message));
            }
            catch
            {
                // never block the UI/scan on logging
            }
        }

        /// <summary>
        /// Get battery for Bluetooth HFP devices using CfgMgr32/DEVPKEY_Bluetooth_Battery.
        /// Returns a dictionary of BT address -> BatteryInfo (level + fallback indicator).
        /// This method works even when the device is connected!
        /// Uses a hybrid priority system:
        /// - High priority: BTHLE devices, HFP headphones (0000111E)
        /// - Low priority (fallback): HFP Audio Gateway (0000111F) - only used if no better source
        /// </summary>
        private static Dictionary<ulong, BatteryInfo> GetBatteryFromPnpDevices()
        {
            var highPriorityResults = new Dictionary<ulong, int>();
            var lowPriorityResults = new Dictionary<ulong, int>(); // HFP AG fallback
            var finalResults = new Dictionary<ulong, BatteryInfo>();

            try
            {
                // Get list of all device instance IDs
                uint bufferLen;
                var result = CfgMgr32.CM_Get_Device_ID_List_Size(out bufferLen, null, 0);
                if (result != CfgMgr32.CR_SUCCESS)
                    return finalResults;

                var buffer = new char[bufferLen];
                result = CfgMgr32.CM_Get_Device_ID_ListW(null, buffer, bufferLen, 0);
                if (result != CfgMgr32.CR_SUCCESS)
                    return finalResults;

                // Parse device IDs (null-separated, double-null terminated)
                var deviceIds = new string(buffer).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                Log($"[PnP] Found {deviceIds.Length} device instances total");

                int scannedCount = 0;
                int batteryCount = 0;

                // Process ALL Bluetooth devices - BTHENUM, BTHLE, BTHLEDEVICE
                foreach (var deviceId in deviceIds)
                {
                    // Check if this is any Bluetooth device
                    bool isBtDevice = deviceId.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) ||
                                      deviceId.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase) ||
                                      deviceId.StartsWith(@"BTHLEDEVICE\", StringComparison.OrdinalIgnoreCase);

                    if (!isBtDevice)
                        continue;

                    // Check if this is HFP Audio Gateway (0000111F) - lower priority/less reliable
                    bool isHfpAudioGateway = deviceId.Contains(@"{0000111f-", StringComparison.OrdinalIgnoreCase);

                    scannedCount++;

                    // Extract BT address from various formats:
                    // BTHENUM: ...&XXXXXXXXXXXX_C00000000
                    // BTHLE: DEV_XXXXXXXXXXXX
                    // BTHLEDEVICE: ..._DEV_VID&...&..._XXXXXXXXXXXX or just the address at the end
                    string? addressPart = null;

                    if (deviceId.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase))
                    {
                        // Format: BTHLE\DEV_E6149E844600\...
                        var afterDev = deviceId.Substring(10); // Skip "BTHLE\DEV_"
                        var slashIdx = afterDev.IndexOf('\\');
                        if (slashIdx > 0)
                        {
                            addressPart = afterDev.Substring(0, slashIdx);
                        }
                    }
                    else if (deviceId.Contains("_DEV_VID&", StringComparison.OrdinalIgnoreCase))
                    {
                        // BLE HID device: look for 12-char hex at the end before the backslash
                        // Example: ...&0217EF_PID&613A_REV&0062_E6149E844600\...
                        var lastBackslash = deviceId.LastIndexOf('\\');
                        if (lastBackslash > 12)
                        {
                            var beforeBackslash = deviceId.Substring(0, lastBackslash);
                            var lastUnderscore = beforeBackslash.LastIndexOf('_');
                            if (lastUnderscore > 0 && beforeBackslash.Length - lastUnderscore - 1 == 12)
                            {
                                addressPart = beforeBackslash.Substring(lastUnderscore + 1);
                            }
                        }
                    }
                    else if (deviceId.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase))
                    {
                        // Classic format: ...&XXXXXXXXXXXX_C00000000
                        var underscoreIdx = deviceId.LastIndexOf('_');
                        if (underscoreIdx > 12)
                        {
                            var ampIdx = deviceId.LastIndexOf('&', underscoreIdx - 1);
                            if (ampIdx >= 0 && underscoreIdx - ampIdx - 1 == 12)
                            {
                                addressPart = deviceId.Substring(ampIdx + 1, 12);
                            }
                        }
                    }
                    
                    if (string.IsNullOrEmpty(addressPart))
                        continue;

                    // Validate it's a hex address
                    if (!ulong.TryParse(addressPart, System.Globalization.NumberStyles.HexNumber, null, out ulong btAddress))
                        continue;

                    // Locate the device node
                    result = CfgMgr32.CM_Locate_DevNodeW(out uint devInst, deviceId, CfgMgr32.CM_LOCATE_DEVNODE_NORMAL);
                    if (result != CfgMgr32.CR_SUCCESS)
                        continue;

                    // Query battery property (DEVPKEY_Bluetooth_Battery)
                    var propKey = new CfgMgr32.DEVPROPKEY
                    {
                        fmtid = CfgMgr32.DEVPKEY_Bluetooth_Battery_fmtid,
                        pid = CfgMgr32.DEVPKEY_Bluetooth_Battery_pid
                    };

                    uint propType = 0;
                    uint propSize = 256;
                    IntPtr propBuffer = Marshal.AllocHGlobal(256);
                    try
                    {
                        result = CfgMgr32.CM_Get_DevNode_PropertyW(devInst, ref propKey, out propType, propBuffer, ref propSize, 0);
                        
                        if (result == CfgMgr32.CR_SUCCESS && propType == CfgMgr32.DEVPROP_TYPE_BYTE)
                        {
                            int batteryLevel = Marshal.ReadByte(propBuffer);
                            if (batteryLevel >= 0 && batteryLevel <= 100)
                            {
                                // Use appropriate dictionary based on priority
                                var targetDict = isHfpAudioGateway ? lowPriorityResults : highPriorityResults;
                                string priorityLabel = isHfpAudioGateway ? "LOW" : "HIGH";

                                // Only update if we don't have this address yet, or this is a higher value
                                if (!targetDict.ContainsKey(btAddress) || targetDict[btAddress] < batteryLevel)
                                {
                                    targetDict[btAddress] = batteryLevel;
                                    batteryCount++;
                                    Log($"[PnP Battery {priorityLabel}] Found battery {batteryLevel}% for addr={addressPart} device={deviceId}");
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(propBuffer);
                    }
                }

                // Build final results: high priority first (not fallback), then low priority (fallback)
                foreach (var kvp in highPriorityResults)
                {
                    finalResults[kvp.Key] = new BatteryInfo { Level = kvp.Value, IsFallback = false };
                }

                // Add low priority only if high priority doesn't have the address (mark as fallback)
                int fallbackCount = 0;
                foreach (var kvp in lowPriorityResults)
                {
                    if (!highPriorityResults.ContainsKey(kvp.Key))
                    {
                        finalResults[kvp.Key] = new BatteryInfo { Level = kvp.Value, IsFallback = true };
                        fallbackCount++;
                        Log($"[PnP Fallback] Using HFP AG battery {kvp.Value}% for addr={kvp.Key:X12} (no better source)");
                    }
                }

                Log($"[PnP] Scanned {scannedCount} BT devices, found {batteryCount} with battery (high={highPriorityResults.Count}, fallback={fallbackCount})");
            }
            catch (Exception ex)
            {
                // Log errors but don't crash
                App.LogToFile("PnP Error", ex);
            }

            return finalResults;
        }

        /// <summary>
        /// Convert a BT address (ulong) to a device name lookup key
        /// </summary>
        private static string FormatBtAddress(ulong address)
        {
            // Format as XX:XX:XX:XX:XX:XX
            var bytes = BitConverter.GetBytes(address);
            return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                bytes[5], bytes[4], bytes[3], bytes[2], bytes[1], bytes[0]);
        }

        // BLE battery is standard only for BLE devices exposing Battery Service.
        // For classic BT devices with proprietary battery, there is no universal solution.
        public async Task<List<WindowsBluetoothDevice>> GetConnectedDevicesAsync()
        {
            // Get battery levels from PnP device tree (HFP devices with DEVPKEY_Bluetooth_Battery)
            var pnpBatteries = GetBatteryFromPnpDevices();
            Log($"[PnP Batteries] Got {pnpBatteries.Count} batteries from PnP");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // Faster timeout
            
            // Run all device scans in parallel for speed
            var bleTask = GetPairedBleDevicesAsync(cts.Token, pnpBatteries);
            var classicTask = GetPairedClassicBluetoothDevicesAsync(cts.Token, pnpBatteries);
            var aepTask = GetPairedAepDevicesAsync(cts.Token);
            
            await Task.WhenAll(bleTask, classicTask, aepTask);
            
            var ble = bleTask.Result;
            var classic = classicTask.Result;
            var aep = aepTask.Result;

            // Merge by name to avoid duplicates (many devices appear in multiple enumerations)
            var merged = new Dictionary<string, WindowsBluetoothDevice>(StringComparer.OrdinalIgnoreCase);

            foreach (var d in classic)
            {
                merged[d.Name] = d;
            }

            foreach (var d in aep)
            {
                if (merged.TryGetValue(d.Name, out var existing))
                {
                    existing.IsConnected = existing.IsConnected || d.IsConnected;
                    existing.BatteryLevel ??= d.BatteryLevel;
                    if (string.IsNullOrWhiteSpace(existing.Id))
                        existing.Id = d.Id;
                }
                else
                {
                    merged[d.Name] = d;
                }
            }

            foreach (var d in ble)
            {
                if (merged.TryGetValue(d.Name, out var existing))
                {
                    existing.IsConnected = existing.IsConnected || d.IsConnected;
                    existing.BatteryLevel ??= d.BatteryLevel;
                    if (string.IsNullOrWhiteSpace(existing.Id))
                        existing.Id = d.Id;
                }
                else
                {
                    merged[d.Name] = d;
                }
            }

            return merged.Values
                .OrderByDescending(d => d.IsConnected)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsPropertyKeySyntaxError(Exception ex)
        {
            if (ex is COMException com)
            {
                // 0x8002802B: property key syntax error (canonical name not recognized)
                return (uint)com.HResult == 0x8002802B;
            }

            return false;
        }

        private static async Task<List<WindowsBluetoothDevice>> GetPairedBleDevicesAsync(CancellationToken cancellationToken, Dictionary<ulong, BatteryInfo> pnpBatteries)
        {
            var results = new List<WindowsBluetoothDevice>();

            // Paired BLE devices
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection deviceInfos;

            try
            {
                deviceInfos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
                Log($"[BLE Scan] Found {deviceInfos.Count} paired BLE devices");
            }
            catch (OperationCanceledException)
            {
                return results;
            }
            catch
            {
                return results;
            }

            foreach (var deviceInfo in deviceInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = deviceInfo.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!IsUserFacingDeviceName(name))
                    continue;

                BluetoothLEDevice? bleDevice = null;
                try
                {
                    bleDevice = await BluetoothLEDevice.FromIdAsync(deviceInfo.Id).AsTask(cancellationToken);
                    if (bleDevice == null)
                        continue;

                    // Try to get battery from PnP using BLE address
                    int? battery = null;
                    bool isFallback = false;
                    ulong bleAddress = bleDevice.BluetoothAddress;
                    string bleAddrHex = bleAddress.ToString("X12");
                    
                    Log($"[BLE Processing] Processing: '{name}' connected={bleDevice.ConnectionStatus} addr={bleAddrHex}");

                    if (pnpBatteries.TryGetValue(bleAddress, out BatteryInfo batteryInfo))
                    {
                        battery = batteryInfo.Level;
                        isFallback = batteryInfo.IsFallback;
                        Log($"[Battery PnP Hit] Got battery {batteryInfo.Level}% from PnP for BLE '{name}' (fallback={isFallback})");
                    }

                    var item = new WindowsBluetoothDevice
                    {
                        Name = name,
                        Id = deviceInfo.Id,
                        IsConnected = bleDevice.ConnectionStatus == BluetoothConnectionStatus.Connected,
                        BatteryLevel = battery,
                        IsBatteryFallback = isFallback
                    };

                    results.Add(item);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore per-device failures
                }
                finally
                {
                    bleDevice?.Dispose();
                }
            }

            // Prefer connected devices first
            return results
                .OrderByDescending(d => d.IsConnected)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<List<WindowsBluetoothDevice>> GetPairedClassicBluetoothDevicesAsync(CancellationToken cancellationToken, Dictionary<ulong, BatteryInfo> pnpBatteries)
        {
            var results = new List<WindowsBluetoothDevice>();

            // Paired classic Bluetooth devices
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            DeviceInformationCollection deviceInfos;

            try
            {
                deviceInfos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return results;
            }
            catch
            {
                return results;
            }

            foreach (var deviceInfo in deviceInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = deviceInfo.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!IsUserFacingDeviceName(name))
                    continue;

                BluetoothDevice? btDevice = null;
                try
                {
                    btDevice = await BluetoothDevice.FromIdAsync(deviceInfo.Id).AsTask(cancellationToken);
                    if (btDevice == null)
                        continue;

                    // First try to get battery from PnP (via DEVPKEY_Bluetooth_Battery)
                    int? battery = null;
                    bool isFallback = false;
                    if (pnpBatteries.TryGetValue(btDevice.BluetoothAddress, out BatteryInfo batteryInfo))
                    {
                        battery = batteryInfo.Level;
                        isFallback = batteryInfo.IsFallback;
                    }
                    // Fallback: try RFCOMM for Sony/XM devices (only works if not already connected)
                    else if (ShouldProbeBattery(name) && btDevice.ConnectionStatus != BluetoothConnectionStatus.Connected)
                    {
                        battery = await TryReadBatteryFromClassicDeviceAsync(deviceInfo.Id, name, cancellationToken);
                    }

                    var item = new WindowsBluetoothDevice
                    {
                        Name = name,
                        Id = deviceInfo.Id,
                        IsConnected = btDevice.ConnectionStatus == BluetoothConnectionStatus.Connected,
                        BatteryLevel = battery,
                        IsBatteryFallback = isFallback
                    };

                    results.Add(item);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore per-device failures
                }
                finally
                {
                    btDevice?.Dispose();
                }
            }

            return results
                .OrderByDescending(d => d.IsConnected)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<int?> TryReadBatteryFromClassicDeviceAsync(string deviceId, string name, CancellationToken cancellationToken)
        {
            // Use RFCOMM + AT Commands (IPHONEACCEV) to get battery - works for Sony, JBL, and many other headsets
            // NOTE: This only works when device is NOT connected (socket will be busy otherwise)
            try
            {
                // Get the BluetoothDevice
                var btDevice = await BluetoothDevice.FromIdAsync(deviceId).AsTask(cancellationToken);
                if (btDevice == null)
                    return null;

                // Get RFCOMM services - we need Hands-Free (HFP) or Headset (HSP) profile
                var rfcommResult = await btDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);

                foreach (var service in rfcommResult.Services)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var uuid = service.ServiceId.Uuid.ToString().ToUpperInvariant();
                    
                    // HFP Hands-Free UUID: 0000111E or 0000111F
                    // HSP Headset UUID: 00001108 or 00001112
                    if (!uuid.StartsWith("0000111E") && !uuid.StartsWith("0000111F") && 
                        !uuid.StartsWith("00001108") && !uuid.StartsWith("00001112"))
                        continue;

                    try
                    {
                        var battery = await TryReadBatteryFromRfcommServiceAsync(service, name, cancellationToken);
                        if (battery.HasValue)
                        {
                            btDevice.Dispose();
                            return battery;
                        }
                    }
                    catch
                    {
                        // Try next service
                    }
                }

                btDevice.Dispose();
            }
            catch
            {
                // Silently fail - PnP method is preferred anyway
            }

            return null;
        }

        private static async Task<int?> TryReadBatteryFromRfcommServiceAsync(RfcommDeviceService service, string name, CancellationToken cancellationToken)
        {
            StreamSocket? socket = null;
            try
            {
                socket = new StreamSocket();
                
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(TimeSpan.FromSeconds(3));
                
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName)
                    .AsTask(connectCts.Token);

                // Listen for AT commands from the device
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readCts.CancelAfter(TimeSpan.FromSeconds(5));

                var buffer = new Windows.Storage.Streams.Buffer(2048);
                var accumulated = string.Empty;
                var startTime = DateTime.UtcNow;

                while ((DateTime.UtcNow - startTime).TotalSeconds < 4 && !readCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var readTask = socket.InputStream.ReadAsync(buffer, buffer.Capacity, InputStreamOptions.Partial).AsTask(readCts.Token);
                        var result = await readTask;

                        if (result.Length > 0)
                        {
                            var reader = DataReader.FromBuffer(result);
                            var data = reader.ReadString(result.Length);
                            accumulated += data;

                            // Send OK response to keep the conversation going
                            var okBytes = CryptographicBuffer.ConvertStringToBinary("\r\nOK\r\n", BinaryStringEncoding.Utf8);
                            await socket.OutputStream.WriteAsync(okBytes).AsTask(readCts.Token);

                            // Check for IPHONEACCEV (Apple battery command used by many headphones)
                            if (accumulated.Contains("IPHONEACCEV"))
                            {
                                var battery = ParseIphoneAccevBattery(accumulated);
                                if (battery.HasValue)
                                    return battery;
                            }

                            // Also check for +BIEV (HFP Battery indicator)
                            if (accumulated.Contains("+BIEV"))
                            {
                                var bievBattery = ParseBievBattery(accumulated);
                                if (bievBattery.HasValue)
                                    return bievBattery;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            catch
            {
                // Socket error - expected when device is already connected
            }
            finally
            {
                socket?.Dispose();
            }

            return null;
        }

        private static int? ParseIphoneAccevBattery(string data)
        {
            // Format: AT+IPHONEACCEV=<num_kv_pairs>,<key>,<value>,...
            // Key 1 = battery level (0-9, representing 10-100% in 10% increments)
            try
            {
                var idx = data.IndexOf("IPHONEACCEV");
                if (idx < 0) return null;

                var cmd = data.Substring(idx);
                var equalIdx = cmd.IndexOf('=');
                if (equalIdx < 0) return null;

                var values = cmd.Substring(equalIdx + 1).Split(',', '\r', '\n');
                if (values.Length < 3) return null;

                // Parse key-value pairs
                if (int.TryParse(values[0].Trim(), out var numPairs) && numPairs >= 1)
                {
                    for (int i = 0; i < numPairs && (i * 2 + 2) < values.Length; i++)
                    {
                        if (int.TryParse(values[i * 2 + 1].Trim(), out var key) && key == 1) // Key 1 = battery
                        {
                            if (int.TryParse(values[i * 2 + 2].Trim(), out var level))
                            {
                                // Level is 0-9, convert to percentage (add 1, multiply by 10)
                                return (level + 1) * 10;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static int? ParseBievBattery(string data)
        {
            // Format: +BIEV: 2,<level> where level is 0-100
            try
            {
                var idx = data.IndexOf("+BIEV:");
                if (idx < 0) return null;

                var cmd = data.Substring(idx + 6).Trim();
                var parts = cmd.Split(',', '\r', '\n');
                if (parts.Length >= 2)
                {
                    if (int.TryParse(parts[0].Trim(), out var indicator) && indicator == 2) // 2 = battery
                    {
                        if (int.TryParse(parts[1].Trim(), out var level) && level >= 0 && level <= 100)
                        {
                            return level;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task<List<WindowsBluetoothDevice>> GetPairedAepDevicesAsync(CancellationToken cancellationToken)
        {
            var results = new List<WindowsBluetoothDevice>();

            // AEP (Association Endpoint) entries - sometimes has battery info for some devices
            DeviceInformationCollection deviceInfos;
            try
            {
                // Query paired endpoints (covers BT + BLE endpoints)
                var aqs = "System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
                deviceInfos = await DeviceInformation.FindAllAsync(aqs, null, DeviceInformationKind.AssociationEndpoint)
                    .AsTask(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return results;
            }
            catch
            {
                return results;
            }

            foreach (var deviceInfo in deviceInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = deviceInfo.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!IsUserFacingDeviceName(name))
                    continue;

                int? battery = null;
                bool connected = false;

                // Only probe battery for likely user devices to keep scans quick.
                if (ShouldProbeBattery(name))
                {
                    battery = await TryReadBatteryFromAepAsync(deviceInfo.Id, cancellationToken);
                    connected = (await TryReadBoolPropertyFromAepAsync(deviceInfo.Id, AepConnectedPropertyKeys, cancellationToken)) ?? false;
                }

                results.Add(new WindowsBluetoothDevice
                {
                    Name = name,
                    Id = deviceInfo.Id,
                    IsConnected = connected,
                    BatteryLevel = battery
                });
            }

            return results
                .OrderByDescending(d => d.IsConnected)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsUserFacingDeviceName(string name)
        {
            var excludeKeywords = new[]
            {
                "RFCOMM",
                "Bluetooth Adapter",
                "Bluetooth Enumerator",
                "Generic Bluetooth",
                "Microsoft",
                "HCI",
                "AVRCP",
                "A2DP",
                "Audio Gateway",
            };

            foreach (var keyword in excludeKeywords)
            {
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return name.Length > 2;
        }

        private static async Task<int?> TryReadBleBatteryPercentAsync(BluetoothLEDevice bleDevice, CancellationToken cancellationToken)
        {
            // Standard Battery Service UUID: 0x180F
            // Standard Battery Level Characteristic UUID: 0x2A19
            try
            {
                var servicesResult = await bleDevice
                    .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken);

                if (servicesResult.Status != GattCommunicationStatus.Success)
                    return null;

                var service = servicesResult.Services.FirstOrDefault();
                if (service == null)
                    return null;

                using (service)
                {
                    var charsResult = await service
                        .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken);

                    if (charsResult.Status != GattCommunicationStatus.Success)
                        return null;

                    var characteristic = charsResult.Characteristics.FirstOrDefault();
                    if (characteristic == null)
                        return null;

                    var read = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                    if (read.Status != GattCommunicationStatus.Success)
                        return null;

                    using var reader = DataReader.FromBuffer(read.Value);
                    if (reader.UnconsumedBufferLength < 1)
                        return null;

                    byte battery = reader.ReadByte();
                    if (battery > 100)
                        return null;

                    return battery;
                }
            }
            catch
            {
                return null;
            }
        }

        private static int? TryGetBatteryFromProperties(IReadOnlyDictionary<string, object> properties)
        {
            foreach (var key in BatteryPropertyKeys)
            {
                if (!properties.TryGetValue(key, out var value) || value == null)
                    continue;

                if (TryCoerceBatteryPercent(value, out var battery))
                    return battery;
            }

            return null;
        }

        private static int? TryGetBatteryFromAnyBatteryLikeProperty(IReadOnlyDictionary<string, object> properties)
        {
            // Some vendors/drivers expose a differently-named key; scan for anything with “Battery” in the name.
            foreach (var kvp in properties)
            {
                if (!kvp.Key.Contains("Battery", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (kvp.Value == null)
                    continue;

                if (TryCoerceBatteryPercent(kvp.Value, out var battery))
                    return battery;
            }

            return null;
        }

        private static bool? TryGetBool(IReadOnlyDictionary<string, object> properties, string key)
        {
            if (!properties.TryGetValue(key, out var value) || value == null)
                return null;

            try
            {
                return value switch
                {
                    bool b => b,
                    byte by => by != 0,
                    int i => i != 0,
                    string s when bool.TryParse(s, out var parsed) => parsed,
                    _ => null,
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool TryCoerceBatteryPercent(object value, out int battery)
        {
            battery = 0;

            try
            {
                switch (value)
                {
                    case byte b:
                        battery = b;
                        break;
                    case sbyte sb:
                        battery = sb;
                        break;
                    case short s:
                        battery = s;
                        break;
                    case ushort us:
                        battery = us;
                        break;
                    case int i:
                        battery = i;
                        break;
                    case uint ui:
                        battery = (int)ui;
                        break;
                    case long l:
                        battery = (int)l;
                        break;
                    case string str when int.TryParse(str, out var parsed):
                        battery = parsed;
                        break;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }

            if (battery < 0 || battery > 100)
                return false;

            return true;
        }

        private static bool ShouldProbeBattery(string name)
        {
            // Keep it simple and fast: probe for common headphone/earbud names.
            return name.Contains("Sony", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("XM", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("WF-", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("WH-", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<int?> TryReadBatteryFromAepAsync(string aepId, CancellationToken cancellationToken)
        {
            foreach (var key in BatteryPropertyKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var di = await DeviceInformation
                        .CreateFromIdAsync(aepId, new[] { key }, DeviceInformationKind.AssociationEndpoint)
                        .AsTask(cancellationToken);

                    if (di.Properties.TryGetValue(key, out var value) && value != null && TryCoerceBatteryPercent(value, out var battery))
                        return battery;
                }
                catch (Exception ex) when (IsPropertyKeySyntaxError(ex))
                {
                    // unsupported key on this machine
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    App.LogToFile("AEP Battery Probe", ex);
                }
            }

            return null;
        }

        private static async Task<bool?> TryReadBoolPropertyFromAepAsync(string aepId, IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var di = await DeviceInformation
                        .CreateFromIdAsync(aepId, new[] { key }, DeviceInformationKind.AssociationEndpoint)
                        .AsTask(cancellationToken);

                    var parsed = TryGetBool(di.Properties, key);
                    if (parsed.HasValue)
                        return parsed.Value;
                }
                catch (Exception ex) when (IsPropertyKeySyntaxError(ex))
                {
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
