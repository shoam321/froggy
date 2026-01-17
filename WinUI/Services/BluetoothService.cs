using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BluetoothWidget.Services;

public class BluetoothDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public int? BatteryLevel { get; set; }
    public ulong BluetoothAddress { get; set; }
}

/// <summary>
/// Service for discovering Bluetooth devices and reading battery levels
/// using the Windows PnP device tree (DEVPKEY_Bluetooth_Battery).
/// </summary>
public class BluetoothService
{
    #region CfgMgr32 Interop

    private const uint CR_SUCCESS = 0;
    private const uint CM_LOCATE_DEVNODE_NORMAL = 0;
    private const uint DEVPROP_TYPE_BYTE = 0x00000003;

    // DEVPKEY_Bluetooth_Battery: {104ea319-6ee2-4701-bd47-8ddbf425bbe5}, 2
    private static readonly Guid DEVPKEY_Bluetooth_Battery_fmtid = new("104ea319-6ee2-4701-bd47-8ddbf425bbe5");
    private const uint DEVPKEY_Bluetooth_Battery_pid = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint CM_Get_Device_ID_List_Size(out uint pulLen, string? pszFilter, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint CM_Get_Device_ID_ListW(string? pszFilter, char[] Buffer, uint BufferLen, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        IntPtr PropertyBuffer,
        ref uint PropertyBufferSize,
        uint ulFlags);

    #endregion

    public async Task<List<BluetoothDeviceInfo>> GetBluetoothDevicesAsync()
    {
        // Get battery levels from PnP device tree (HFP devices)
        var pnpBatteries = GetBatteryFromPnpDevices();

        // Get paired classic Bluetooth devices
        var devices = await GetPairedBluetoothDevicesAsync(pnpBatteries);

        return devices
            .OrderByDescending(d => d.IsConnected)
            .ThenByDescending(d => d.BatteryLevel.HasValue)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Get battery for Bluetooth HFP devices using CfgMgr32/DEVPKEY_Bluetooth_Battery.
    /// Returns a dictionary of BT address -> battery level.
    /// </summary>
    private static Dictionary<ulong, int> GetBatteryFromPnpDevices()
    {
        var results = new Dictionary<ulong, int>();

        try
        {
            // Get list of all device instance IDs
            var result = CM_Get_Device_ID_List_Size(out uint bufferLen, null, 0);
            if (result != CR_SUCCESS)
                return results;

            var buffer = new char[bufferLen];
            result = CM_Get_Device_ID_ListW(null, buffer, bufferLen, 0);
            if (result != CR_SUCCESS)
                return results;

            // Parse device IDs (null-separated, double-null terminated)
            var deviceIds = new string(buffer).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

            // Filter for Bluetooth HFP devices - they have BTHENUM\{0000111e-...}
            foreach (var deviceId in deviceIds)
            {
                // HFP UUID is 0000111E (Hands-Free Profile)
                if (!deviceId.StartsWith(@"BTHENUM\{0000111E", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Extract BT address - look for 12-char hex pattern before _C00000000
                string? addressPart = null;
                var underscoreIdx = deviceId.LastIndexOf('_');
                if (underscoreIdx > 12)
                {
                    var ampIdx = deviceId.LastIndexOf('&', underscoreIdx - 1);
                    if (ampIdx >= 0 && underscoreIdx - ampIdx - 1 == 12)
                    {
                        addressPart = deviceId.Substring(ampIdx + 1, 12);
                    }
                }

                if (string.IsNullOrEmpty(addressPart))
                    continue;

                if (!ulong.TryParse(addressPart, System.Globalization.NumberStyles.HexNumber, null, out ulong btAddress))
                    continue;

                // Locate the device node
                result = CM_Locate_DevNodeW(out uint devInst, deviceId, CM_LOCATE_DEVNODE_NORMAL);
                if (result != CR_SUCCESS)
                    continue;

                // Query battery property (DEVPKEY_Bluetooth_Battery)
                var propKey = new DEVPROPKEY
                {
                    fmtid = DEVPKEY_Bluetooth_Battery_fmtid,
                    pid = DEVPKEY_Bluetooth_Battery_pid
                };

                uint propType = 0;
                uint propSize = 256;
                IntPtr propBuffer = Marshal.AllocHGlobal(256);
                try
                {
                    result = CM_Get_DevNode_PropertyW(devInst, ref propKey, out propType, propBuffer, ref propSize, 0);

                    if (result == CR_SUCCESS && propType == DEVPROP_TYPE_BYTE)
                    {
                        int batteryLevel = Marshal.ReadByte(propBuffer);
                        if (batteryLevel >= 0 && batteryLevel <= 100)
                        {
                            results[btAddress] = batteryLevel;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(propBuffer);
                }
            }
        }
        catch
        {
            // Silently fail
        }

        return results;
    }

    private static async Task<List<BluetoothDeviceInfo>> GetPairedBluetoothDevicesAsync(Dictionary<ulong, int> pnpBatteries)
    {
        var results = new List<BluetoothDeviceInfo>();

        try
        {
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var deviceInfos = await DeviceInformation.FindAllAsync(selector);

            foreach (var deviceInfo in deviceInfos)
            {
                var name = deviceInfo.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Filter out system devices
                if (!IsUserFacingDeviceName(name))
                    continue;

                BluetoothDevice? btDevice = null;
                try
                {
                    btDevice = await BluetoothDevice.FromIdAsync(deviceInfo.Id);
                    if (btDevice == null)
                        continue;

                    // Get battery from PnP if available
                    int? battery = null;
                    if (pnpBatteries.TryGetValue(btDevice.BluetoothAddress, out int pnpBattery))
                    {
                        battery = pnpBattery;
                    }

                    results.Add(new BluetoothDeviceInfo
                    {
                        Name = name,
                        Id = deviceInfo.Id,
                        IsConnected = btDevice.ConnectionStatus == BluetoothConnectionStatus.Connected,
                        BatteryLevel = battery,
                        BluetoothAddress = btDevice.BluetoothAddress
                    });
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
        }
        catch
        {
            // Silently fail
        }

        return results;
    }

    private static bool IsUserFacingDeviceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Whitelist: Known consumer brands - always show these
        var knownBrands = new[]
        {
            // Gaming brands
            "ROCCAT", "HyperX", "Razer", "Logitech", "SteelSeries", "Corsair", 
            "Turtle Beach", "Astro", "ASUS", "ROG", "Alienware", "MSI",
            // Audio brands
            "Sony", "Bose", "JBL", "Sennheiser", "Audio-Technica", "Beats", 
            "AirPods", "Samsung", "Jabra", "Anker", "Soundcore", "Skullcandy",
            "Bang & Olufsen", "B&O", "Shure", "Beyerdynamic", "AKG", "Focal",
            "Philips", "Denon", "Pioneer", "Bowers & Wilkins", "Marshall",
            "Harman Kardon", "OnePlus", "Xiaomi", "Huawei", "Oppo", "Vivo",
            "LG", "Motorola", "Nokia", "Edifier", "1MORE", "Audio Pro",
            // Other brands
            "Apple", "Google", "Microsoft Surface", "Plantronics", "Poly",
            "Creative", "Urbanears", "Master & Dynamic", "Grado", "Jaybird",
            "Aftershokz", "Shokz", "Taotronics", "Mpow", "TaoTronics",
            "Aukey", "Tronsmart", "Tribit", "JLab", "Soundpeats", "Tozo"
        };

        foreach (var brand in knownBrands)
        {
            if (name.Contains(brand, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Exclude system/internal device names (only for non-branded devices)
        var lower = name.ToLowerInvariant();
        var excluded = new[]
        {
            "bluetooth", "adapter", "radio", "controller", "host",
            "virtual", "enumerator", "port", "serial", "com",
            "microsoft", "windows", "generic", "unknown",
            "le_", "_le", "-le", "gatt", "rfcomm", "hci", "avrcp", "a2dp"
        };

        foreach (var ex in excluded)
        {
            if (lower.Contains(ex) && !lower.Contains("headset") && !lower.Contains("speaker") && !lower.Contains("audio"))
                return false;
        }

        return name.Length > 2;
    }
}
