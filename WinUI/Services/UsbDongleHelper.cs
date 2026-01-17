using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace BluetoothWidget.Services
{
    public class UsbDongleDevice
    {
        public string Name { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty; // PnP DeviceID string
        public string VendorId { get; set; } = string.Empty; // e.g. "1E7D"
        public string ProductId { get; set; } = string.Empty; // e.g. "0A12"
        public string? Brand { get; set; } // e.g. "ROCCAT" or "HyperX"
        public int? BatteryLevel { get; set; } // Battery percentage (0-100) if available
        public bool? IsCharging { get; set; } // Charging status if available
    }

    public static class UsbDongleHelper
    {
        private static readonly HashSet<string> KnownVendorVids = new(StringComparer.OrdinalIgnoreCase)
        {
            "1E7D", // ROCCAT
            "0951", // Kingston / HyperX
            "03F0", // HP (rebranded HyperX units)
        };

        #region HID API Interop

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            string? enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(
            IntPtr hidDeviceObject,
            ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(
            IntPtr hidDeviceObject,
            byte[] reportBuffer,
            uint reportBufferLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public uint flags;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public uint Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const int DETAIL_DATA_SIZE_64BIT = 8;
        private const int DETAIL_DATA_SIZE_32BIT = 6;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        #endregion

        public static List<UsbDongleDevice> GetUsbDongleDevices()
        {
            var results = new List<UsbDongleDevice>();
            var deviceBatteryMap = new Dictionary<string, (int battery, bool charging)>();

            // First, probe HID devices for battery info
            try
            {
                ProbeHidDevicesForBattery(deviceBatteryMap);
            }
            catch
            {
                // Continue even if HID probing fails
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_%'");
                using var collection = searcher.Get();

                foreach (ManagementObject mo in collection)
                {
                    try
                    {
                        var deviceId = (mo["DeviceID"] as string) ?? string.Empty;
                        var name = (mo["Name"] as string) ?? string.Empty;

                        var vid = ExtractToken(deviceId, "VID_");
                        var pid = ExtractToken(deviceId, "PID_");

                        if (string.IsNullOrEmpty(vid))
                            continue;

                        if (!KnownVendorVids.Contains(vid))
                            continue;

                        string? brand = GetBrandFromVid(vid);

                        // Create device key for battery lookup (VID:PID)
                        var deviceKey = $"{vid}:{pid}";
                        int? batteryLevel = null;
                        bool? isCharging = null;

                        if (deviceBatteryMap.TryGetValue(deviceKey, out var batteryInfo))
                        {
                            batteryLevel = batteryInfo.battery;
                            isCharging = batteryInfo.charging;
                        }

                        results.Add(new UsbDongleDevice
                        {
                            Name = string.IsNullOrWhiteSpace(name) ? $"{brand ?? "Unknown"} USB Dongle" : name,
                            DeviceId = deviceId,
                            VendorId = vid,
                            ProductId = pid,
                            Brand = brand,
                            BatteryLevel = batteryLevel,
                            IsCharging = isCharging
                        });
                    }
                    catch
                    {
                        // skip problematic entries
                    }
                }
            }
            catch
            {
                // Return what we have on failure
            }

            return results;
        }

        /// <summary>
        /// Probe HID devices for battery information using Feature Reports
        /// </summary>
        private static void ProbeHidDevicesForBattery(Dictionary<string, (int battery, bool charging)> batteryMap)
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr deviceInfoSet = SetupDiGetClassDevs(
                ref hidGuid,
                null,
                IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == INVALID_HANDLE_VALUE)
                return;

            try
            {
                uint memberIndex = 0;
                while (true)
                {
                    var deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                    deviceInterfaceData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

                    if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref hidGuid,
                        memberIndex,
                        ref deviceInterfaceData))
                    {
                        break; // No more devices
                    }

                    memberIndex++;

                    // Get the device path
                    uint requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(
                        deviceInfoSet,
                        ref deviceInterfaceData,
                        IntPtr.Zero,
                        0,
                        out requiredSize,
                        IntPtr.Zero);

                    if (requiredSize == 0)
                        continue;

                    IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        // First 4 bytes are cbSize (size depends on platform)
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? DETAIL_DATA_SIZE_64BIT : DETAIL_DATA_SIZE_32BIT);

                        if (SetupDiGetDeviceInterfaceDetail(
                            deviceInfoSet,
                            ref deviceInterfaceData,
                            detailDataBuffer,
                            requiredSize,
                            out _,
                            IntPtr.Zero))
                        {
                            // Device path starts after cbSize (4 bytes on 32-bit, 8 bytes on 64-bit)
                            IntPtr pathPtr = IntPtr.Add(detailDataBuffer, 4);
                            string? devicePath = Marshal.PtrToStringUni(pathPtr);

                            if (!string.IsNullOrEmpty(devicePath))
                            {
                                TryReadBatteryFromHidDevice(devicePath, batteryMap);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        /// <summary>
        /// Try to read battery info from a specific HID device
        /// </summary>
        private static void TryReadBatteryFromHidDevice(
            string devicePath,
            Dictionary<string, (int battery, bool charging)> batteryMap)
        {
            IntPtr handle = CreateFile(
                devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == INVALID_HANDLE_VALUE)
                return;

            try
            {
                // Get device attributes to identify vendor/product
                var attributes = new HIDD_ATTRIBUTES();
                attributes.Size = (uint)Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));

                if (!HidD_GetAttributes(handle, ref attributes))
                    return;

                // Only process known vendors
                string vid = attributes.VendorID.ToString("X4");
                string pid = attributes.ProductID.ToString("X4");

                if (!KnownVendorVids.Contains(vid))
                    return;

                // Try ROCCAT battery probing (Report ID 0x06)
                if (vid.Equals("1E7D", StringComparison.OrdinalIgnoreCase))
                {
                    var battery = TryReadRoccatBattery(handle);
                    if (battery.HasValue)
                    {
                        string deviceKey = $"{vid}:{pid}";
                        batteryMap[deviceKey] = (battery.Value.level, battery.Value.charging);
                    }
                }
            }
            catch
            {
                // Ignore errors for individual devices
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// Read battery from ROCCAT device using Report ID 0x06
        /// </summary>
        private static (int level, bool charging)? TryReadRoccatBattery(IntPtr handle)
        {
            try
            {
                // Try Report ID 0x06 (standard for ROCCAT battery/status)
                byte[] buffer = new byte[64];
                buffer[0] = 0x06; // Report ID

                if (HidD_GetFeature(handle, buffer, (uint)buffer.Length))
                {
                    // Parse response
                    if (buffer[0] == 0x06) // Confirm Report ID
                    {
                        int batteryLevel = buffer[1];
                        bool charging = buffer[2] != 0;

                        if (batteryLevel >= 0 && batteryLevel <= 100)
                        {
                            return (batteryLevel, charging);
                        }
                    }
                }

                // Try Report ID 0x04 as fallback (some models use this for telemetry)
                buffer = new byte[64];
                buffer[0] = 0x04;

                if (HidD_GetFeature(handle, buffer, (uint)buffer.Length))
                {
                    if (buffer[0] == 0x04)
                    {
                        // Some devices report battery in different positions
                        // Try byte 1 for battery level
                        int batteryLevel = buffer[1];
                        if (batteryLevel >= 0 && batteryLevel <= 100)
                        {
                            // Note: Charging status defaults to false when unknown (Report 0x04 doesn't include it)
                            return (batteryLevel, false);
                        }
                    }
                }
            }
            catch
            {
                // Silent failure
            }

            return null;
        }

        private static string? GetBrandFromVid(string vid)
        {
            if (vid.Equals("1E7D", StringComparison.OrdinalIgnoreCase))
                return "ROCCAT";
            if (vid.Equals("0951", StringComparison.OrdinalIgnoreCase) || 
                vid.Equals("03F0", StringComparison.OrdinalIgnoreCase))
                return "HyperX";
            return null;
        }

        private static string ExtractToken(string deviceId, string token)
        {
            var idx = deviceId.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return string.Empty;

            var start = idx + token.Length;
            var end = start;

            while (end < deviceId.Length && IsHexDigit(deviceId[end]))
                end++;

            if (end <= start)
                return string.Empty;

            return deviceId.Substring(start, end - start);
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
        }
    }
}
