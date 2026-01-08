using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;

namespace BluetoothWidget
{
    /// <summary>
    /// Monitors network speed by tracking bytes transferred over time.
    /// Provides real-time download/upload speed measurements.
    /// </summary>
    public class NetworkSpeedHelper
    {
        private long _lastBytesReceived;
        private long _lastBytesSent;
        private DateTime _lastCheckTime;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Current download speed in bytes per second.
        /// </summary>
        public double DownloadSpeedBps { get; private set; }

        /// <summary>
        /// Current upload speed in bytes per second.
        /// </summary>
        public double UploadSpeedBps { get; private set; }

        /// <summary>
        /// Current ping latency in milliseconds.
        /// </summary>
        public long PingMs { get; private set; }

        /// <summary>
        /// Whether the network is currently connected.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Name of the active network adapter.
        /// </summary>
        public string AdapterName { get; private set; } = "Unknown";

        public NetworkSpeedHelper()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _lastCheckTime = DateTime.Now;
            InitializeByteCounters();
        }

        /// <summary>
        /// Initializes byte counters from active network interface.
        /// </summary>
        private void InitializeByteCounters()
        {
            try
            {
                var activeInterface = GetActiveNetworkInterface();
                if (activeInterface != null)
                {
                    var stats = activeInterface.GetIPv4Statistics();
                    _lastBytesReceived = stats.BytesReceived;
                    _lastBytesSent = stats.BytesSent;
                    AdapterName = activeInterface.Name;
                    IsConnected = true;
                }
            }
            catch
            {
                IsConnected = false;
            }
        }

        /// <summary>
        /// Gets the active network interface (WiFi or Ethernet).
        /// Prioritizes WiFi connections.
        /// </summary>
        private NetworkInterface? GetActiveNetworkInterface()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                           (n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                            n.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                .ToList();

            return interfaces.FirstOrDefault();
        }

        /// <summary>
        /// Updates speed measurements by calculating bytes transferred since last check.
        /// Call this periodically (every 1-2 seconds) for accurate readings.
        /// </summary>
        public async Task UpdateSpeedAsync()
        {
            try
            {
                var activeInterface = GetActiveNetworkInterface();
                
                if (activeInterface == null)
                {
                    IsConnected = false;
                    DownloadSpeedBps = 0;
                    UploadSpeedBps = 0;
                    AdapterName = "No Connection";
                    return;
                }

                IsConnected = true;
                AdapterName = activeInterface.Name;

                var stats = activeInterface.GetIPv4Statistics();
                var currentTime = DateTime.Now;
                var elapsedSeconds = (currentTime - _lastCheckTime).TotalSeconds;

                if (elapsedSeconds > 0.1) // Minimum interval to avoid division issues
                {
                    var bytesReceivedDelta = stats.BytesReceived - _lastBytesReceived;
                    var bytesSentDelta = stats.BytesSent - _lastBytesSent;

                    // Handle counter reset (happens after ~4GB on 32-bit counters)
                    if (bytesReceivedDelta < 0) bytesReceivedDelta = 0;
                    if (bytesSentDelta < 0) bytesSentDelta = 0;

                    DownloadSpeedBps = bytesReceivedDelta / elapsedSeconds;
                    UploadSpeedBps = bytesSentDelta / elapsedSeconds;

                    _lastBytesReceived = stats.BytesReceived;
                    _lastBytesSent = stats.BytesSent;
                    _lastCheckTime = currentTime;
                }

                // Update ping asynchronously
                await UpdatePingAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating network speed: {ex.Message}");
                IsConnected = false;
            }
        }

        /// <summary>
        /// Measures ping latency to a reliable server.
        /// </summary>
        private async Task UpdatePingAsync()
        {
            try
            {
                using var ping = new Ping();
                // Use Google's DNS as a reliable ping target
                var reply = await ping.SendPingAsync("8.8.8.8", 1000);
                
                if (reply.Status == IPStatus.Success)
                {
                    PingMs = reply.RoundtripTime;
                }
                else
                {
                    PingMs = -1; // Indicates ping failed
                }
            }
            catch
            {
                PingMs = -1;
            }
        }

        /// <summary>
        /// Formats speed in human-readable format (Kbps, Mbps, etc.)
        /// </summary>
        public static string FormatSpeed(double bytesPerSecond)
        {
            double bitsPerSecond = bytesPerSecond * 8;

            if (bitsPerSecond >= 1_000_000_000)
                return $"{bitsPerSecond / 1_000_000_000:F1} Gbps";
            if (bitsPerSecond >= 1_000_000)
                return $"{bitsPerSecond / 1_000_000:F1} Mbps";
            if (bitsPerSecond >= 1_000)
                return $"{bitsPerSecond / 1_000:F1} Kbps";
            
            return $"{bitsPerSecond:F0} bps";
        }

        /// <summary>
        /// Gets a summary string of current network status.
        /// </summary>
        public string GetStatusSummary()
        {
            if (!IsConnected)
                return "No Connection";

            var download = FormatSpeed(DownloadSpeedBps);
            var upload = FormatSpeed(UploadSpeedBps);
            var pingText = PingMs >= 0 ? $"{PingMs}ms" : "N/A";

            return $"↓{download} ↑{upload} | {pingText}";
        }
    }
}
