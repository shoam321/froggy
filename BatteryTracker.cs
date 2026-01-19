using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BluetoothWidget
{
    public class BatteryReading
    {
        public DateTime Timestamp { get; set; }
        public int BatteryLevel { get; set; }
    }

    public class DeviceBatteryHistory
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public List<BatteryReading> Readings { get; set; } = new();
    }

    public class BatteryStats
    {
        public double? DrainRatePerHour { get; set; }  // % per hour
        public TimeSpan? EstimatedTimeRemaining { get; set; }
        public TimeSpan? TimeSinceLastCharge { get; set; }
        public int? LastFullChargeLevel { get; set; }
    }

    public static class BatteryTracker
    {
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothWidget");
        
        private static readonly string HistoryFile = Path.Combine(DataDir, "battery_history.json");
        private static readonly string LogFile = Path.Combine(DataDir, "battery_log.txt");
        
        private static Dictionary<string, DeviceBatteryHistory> _history = new();
        private static readonly object _lock = new();

        static BatteryTracker()
        {
            LoadHistory();
        }

        private static void LoadHistory()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                if (File.Exists(HistoryFile))
                {
                    var json = File.ReadAllText(HistoryFile);
                    var list = JsonSerializer.Deserialize<List<DeviceBatteryHistory>>(json);
                    if (list != null)
                    {
                        _history = list.ToDictionary(h => h.DeviceId, h => h);
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogToFile("BatteryTracker.LoadHistory", ex);
            }
        }

        private static void SaveHistory()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var json = JsonSerializer.Serialize(_history.Values.ToList(), new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(HistoryFile, json);
            }
            catch (Exception ex)
            {
                App.LogToFile("BatteryTracker.SaveHistory", ex);
            }
        }

        /// <summary>
        /// Record a battery reading for a device
        /// </summary>
        public static void RecordBattery(string deviceId, string deviceName, int batteryLevel)
        {
            lock (_lock)
            {
                try
                {
                    if (!_history.TryGetValue(deviceId, out var history))
                    {
                        history = new DeviceBatteryHistory
                        {
                            DeviceId = deviceId,
                            DeviceName = deviceName
                        };
                        _history[deviceId] = history;
                    }

                    // Update device name if changed
                    history.DeviceName = deviceName;

                    var now = DateTime.Now;
                    var lastReading = history.Readings.LastOrDefault();

                    // Only record if battery changed or first reading or at least 5 min passed
                    bool shouldRecord = lastReading == null ||
                                       lastReading.BatteryLevel != batteryLevel ||
                                       (now - lastReading.Timestamp).TotalMinutes >= 5;

                    if (shouldRecord)
                    {
                        var reading = new BatteryReading
                        {
                            Timestamp = now,
                            BatteryLevel = batteryLevel
                        };
                        history.Readings.Add(reading);

                        // Log the change
                        LogBatteryChange(deviceName, lastReading?.BatteryLevel, batteryLevel, lastReading?.Timestamp);

                        // Keep only last 7 days of readings
                        var cutoff = now.AddDays(-7);
                        history.Readings = history.Readings.Where(r => r.Timestamp > cutoff).ToList();

                        SaveHistory();
                    }
                }
                catch (Exception ex)
                {
                    App.LogToFile("BatteryTracker.RecordBattery", ex);
                }
            }
        }

        private static void LogBatteryChange(string deviceName, int? previousLevel, int currentLevel, DateTime? previousTime)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var now = DateTime.Now;
                string logEntry;

                if (previousLevel.HasValue && previousTime.HasValue)
                {
                    var elapsed = now - previousTime.Value;
                    var diff = previousLevel.Value - currentLevel;
                    
                    if (diff > 0)
                    {
                        // Battery drained
                        var drainRate = diff / elapsed.TotalHours;
                        var timeRemaining = currentLevel / drainRate;
                        logEntry = $"{now:yyyy-MM-dd HH:mm:ss} | {deviceName} | {previousLevel}% → {currentLevel}% | " +
                                  $"Drained {diff}% in {FormatTimeSpan(elapsed)} | " +
                                  $"Rate: {drainRate:F1}%/hr | " +
                                  $"Est. remaining: {FormatTimeSpan(TimeSpan.FromHours(timeRemaining))}";
                    }
                    else if (diff < 0)
                    {
                        // Battery charged
                        logEntry = $"{now:yyyy-MM-dd HH:mm:ss} | {deviceName} | {previousLevel}% → {currentLevel}% | " +
                                  $"CHARGED +{-diff}% in {FormatTimeSpan(elapsed)}";
                    }
                    else
                    {
                        // No change, just time update
                        logEntry = $"{now:yyyy-MM-dd HH:mm:ss} | {deviceName} | {currentLevel}% | " +
                                  $"No change for {FormatTimeSpan(elapsed)}";
                    }
                }
                else
                {
                    logEntry = $"{now:yyyy-MM-dd HH:mm:ss} | {deviceName} | {currentLevel}% | First reading";
                }

                File.AppendAllText(LogFile, logEntry + Environment.NewLine);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        /// <summary>
        /// Get battery statistics for a device
        /// </summary>
        public static BatteryStats GetStats(string deviceId)
        {
            lock (_lock)
            {
                var stats = new BatteryStats();
                try
                {
                    if (!_history.TryGetValue(deviceId, out var history) || history.Readings.Count < 2)
                        return stats;

                    var readings = history.Readings.OrderBy(r => r.Timestamp).ToList();
                    var now = DateTime.Now;

                    // Use a moving window (last 60 minutes) for drain rate calculation
                    var windowMinutes = 60;
                    var windowStart = now.AddMinutes(-windowMinutes);
                    var windowReadings = readings.Where(r => r.Timestamp >= windowStart).ToList();
                    if (windowReadings.Count < 2)
                    {
                        // Not enough readings in window, fall back to all readings since last charge
                        // Find the last "charge" event (where battery went UP or was at 100%)
                        int chargeIndex = -1;
                        for (int i = readings.Count - 1; i > 0; i--)
                        {
                            if (readings[i].BatteryLevel > readings[i - 1].BatteryLevel || readings[i].BatteryLevel >= 95)
                            {
                                chargeIndex = i;
                                break;
                            }
                        }
                        windowReadings = chargeIndex >= 0 ? readings.Skip(chargeIndex).ToList() : readings;
                    }

                    if (windowReadings.Count >= 2)
                    {
                        var first = windowReadings.First();
                        var last = windowReadings.Last();
                        var elapsed = last.Timestamp - first.Timestamp;
                        var drained = first.BatteryLevel - last.BatteryLevel;

                        stats.LastFullChargeLevel = first.BatteryLevel;
                        stats.TimeSinceLastCharge = now - first.Timestamp;

                        if (drained > 0 && elapsed.TotalHours > 0.1) // At least 6 minutes of data
                        {
                            stats.DrainRatePerHour = drained / elapsed.TotalHours;
                            if (stats.DrainRatePerHour > 0.1) // Meaningful drain rate
                            {
                                var hoursRemaining = last.BatteryLevel / stats.DrainRatePerHour.Value;
                                stats.EstimatedTimeRemaining = TimeSpan.FromHours(hoursRemaining);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.LogToFile("BatteryTracker.GetStats", ex);
                }
                return stats;
            }
        }

        /// <summary>
        /// Get a summary string for display in UI
        /// </summary>
        public static string GetSummaryText(string deviceId)
        {
            lock (_lock)
            {
                var stats = GetStats(deviceId);
                var parts = new List<string>();

                if (stats.DrainRatePerHour.HasValue && stats.DrainRatePerHour.Value > 0)
                {
                    double perHour = stats.DrainRatePerHour.Value;
                    double per10Min = perHour / 6.0;
                    parts.Add($"-{per10Min:F1}%/10min");
                    if (stats.EstimatedTimeRemaining.HasValue)
                    {
                        parts.Add($"~{FormatTimeSpan(stats.EstimatedTimeRemaining.Value)} left");
                    }
                }
                else
                {
                    // No drain detected - show tracking status
                    if (_history.TryGetValue(deviceId, out var history) && history.Readings.Count > 0)
                    {
                        var readings = history.Readings.OrderBy(r => r.Timestamp).ToList();
                        var firstReading = readings.First();
                        var lastReading = readings.Last();
                        var elapsed = DateTime.Now - firstReading.Timestamp;
                        if (elapsed.TotalMinutes < 2)
                        {
                            parts.Add("Tracking...");
                        }
                        else if (lastReading.BatteryLevel == 100)
                        {
                            parts.Add($"Fully charged");
                        }
                        else
                        {
                            parts.Add($"Stable ({readings.Count} readings)");
                        }
                    }
                }

                return parts.Count > 0 ? string.Join(" | ", parts) : "";
            }
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {ts.Hours}h";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{(int)ts.TotalMinutes}m";
        }

        /// <summary>
        /// Get the log file path for user reference
        /// </summary>
        public static string GetLogFilePath() => LogFile;
    }
}
