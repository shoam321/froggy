using System;
using System.Collections.Generic;
using System.Management;

namespace BluetoothWidget.Services
{
    public class UsbDongleDevice
    {
        public string Name { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty; // PnP DeviceID string
        public string VendorId { get; set; } = string.Empty; // e.g. "1E7D"
        public string ProductId { get; set; } = string.Empty; // e.g. "0A12"
        public string? Brand { get; set; } // e.g. "ROCCAT" or "HyperX"
    }

    public static class UsbDongleHelper
    {
        private static readonly HashSet<string> KnownVendorVids = new(StringComparer.OrdinalIgnoreCase)
        {
            "1E7D", // ROCCAT
            "0951", // Kingston / HyperX
            "03F0", // HP (rebranded HyperX units)
        };

        public static List<UsbDongleDevice> GetUsbDongleDevices()
        {
            var results = new List<UsbDongleDevice>();

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

                        string? brand = vid.Equals("1E7D", StringComparison.OrdinalIgnoreCase) ? "ROCCAT" :
                                         (vid.Equals("0951", StringComparison.OrdinalIgnoreCase) || vid.Equals("03F0", StringComparison.OrdinalIgnoreCase)) ? "HyperX" :
                                         null;

                        results.Add(new UsbDongleDevice
                        {
                            Name = string.IsNullOrWhiteSpace(name) ? $"{brand ?? "USB Dongle"} (Dongle)" : name,
                            DeviceId = deviceId,
                            VendorId = vid,
                            ProductId = pid,
                            Brand = brand
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
