using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BluetoothWidget
{
    /// <summary>
    /// Text size levels for UI readability
    /// </summary>
    public enum TextSizeLevel
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    public class DeviceSettingsData
    {
        public Dictionary<string, string> CustomNames { get; set; } = new();
        
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TextSizeLevel TextSize { get; set; } = TextSizeLevel.Medium;
    }

    public static class DeviceSettings
    {
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothWidget");
        
        private static readonly string SettingsFile = Path.Combine(DataDir, "device_settings.json");
        
        private static DeviceSettingsData _settings = new();
        private static readonly object _lock = new();

        static DeviceSettings()
        {
            Load();
        }

        private static void Load()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var data = JsonSerializer.Deserialize<DeviceSettingsData>(json);
                    if (data != null)
                    {
                        _settings = data;
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogToFile("DeviceSettings.Load", ex);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                App.LogToFile("DeviceSettings.Save", ex);
            }
        }

        /// <summary>
        /// Get the display name for a device (custom name if set, otherwise original)
        /// </summary>
        public static string GetDisplayName(string deviceId, string originalName)
        {
            lock (_lock)
            {
                if (_settings.CustomNames.TryGetValue(deviceId, out var customName) && !string.IsNullOrWhiteSpace(customName))
                {
                    return customName;
                }
                return originalName;
            }
        }

        /// <summary>
        /// Set a custom name for a device
        /// </summary>
        public static void SetCustomName(string deviceId, string? customName)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(customName))
                {
                    _settings.CustomNames.Remove(deviceId);
                }
                else
                {
                    _settings.CustomNames[deviceId] = customName.Trim();
                }
                Save();
            }
        }

        /// <summary>
        /// Get the saved UI scale (zoom level)
        /// </summary>
        public static double GetUIScale()
        {
            lock (_lock)
            {
                // Convert TextSize to scale factor for backward compatibility
                return _settings.TextSize switch
                {
                    TextSizeLevel.Small => 0.85,
                    TextSizeLevel.Large => 1.2,
                    _ => 1.0
                };
            }
        }

        /// <summary>
        /// Get the current text size level
        /// </summary>
        public static TextSizeLevel GetTextSize()
        {
            lock (_lock)
            {
                return _settings.TextSize;
            }
        }

        /// <summary>
        /// Set the text size level
        /// </summary>
        public static void SetTextSize(TextSizeLevel size)
        {
            lock (_lock)
            {
                _settings.TextSize = size;
                Save();
            }
        }

        /// <summary>
        /// Cycle to the next text size level (Small -> Medium -> Large -> Small)
        /// </summary>
        public static TextSizeLevel CycleTextSize()
        {
            lock (_lock)
            {
                _settings.TextSize = _settings.TextSize switch
                {
                    TextSizeLevel.Small => TextSizeLevel.Medium,
                    TextSizeLevel.Medium => TextSizeLevel.Large,
                    TextSizeLevel.Large => TextSizeLevel.Small,
                    _ => TextSizeLevel.Medium
                };
                Save();
                return _settings.TextSize;
            }
        }

        /// <summary>
        /// Check if a device has a custom name
        /// </summary>
        public static bool HasCustomName(string deviceId)
        {
            lock (_lock)
            {
                return _settings.CustomNames.ContainsKey(deviceId) && 
                       !string.IsNullOrWhiteSpace(_settings.CustomNames[deviceId]);
            }
        }

        /// <summary>
        /// Clear custom name (revert to original)
        /// </summary>
        public static void ClearCustomName(string deviceId)
        {
            SetCustomName(deviceId, null);
        }
    }
}
