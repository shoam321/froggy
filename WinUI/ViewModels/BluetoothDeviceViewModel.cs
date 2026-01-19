using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using BluetoothWidget.Services;

namespace BluetoothWidget.ViewModels;

public partial class BluetoothDeviceViewModel : ObservableObject
    // Failsafe test: Simulate battery drainage and verify summary output
    public static void RunDrainageTest()
    {
        string testId = "TEST-DEVICE-123";
        string testName = "Test Headphones";
        // Clear any previous history for test device
        BluetoothWidget.BatteryTracker.RecordBattery(testId, testName, 100);
        DateTime now = DateTime.Now;
        // Simulate readings every 10 minutes, draining 5% each time
        for (int i = 1; i <= 12; i++)
        {
            int level = 100 - i * 5;
            DateTime ts = now.AddMinutes(i * 10);
            // Directly add to history for test
            var field = typeof(BluetoothWidget.BatteryTracker).GetField("_history", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (field != null)
            {
                var dict = field.GetValue(null) as System.Collections.IDictionary;
                if (dict != null)
                {
                    if (!dict.Contains(testId))
                    {
                        dict[testId] = new BluetoothWidget.DeviceBatteryHistory { DeviceId = testId, DeviceName = testName, Readings = new System.Collections.Generic.List<BluetoothWidget.BatteryReading>() };
                    }
                    var hist = dict[testId] as BluetoothWidget.DeviceBatteryHistory;
                    if (hist != null)
                    {
                        hist.Readings.Add(new BluetoothWidget.BatteryReading { Timestamp = ts, BatteryLevel = level });
                    }
                }
            }
        }
        // Get summary text for test device
        string summary = BluetoothWidget.BatteryTracker.GetSummaryText(testId);
        // Log result to workspace for review
        var wsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "drainage_test_log.txt");
        System.IO.File.AppendAllText(wsPath, $"Drainage Test Summary: {summary}\n");
    }
{
    private readonly BluetoothDeviceInfo? _device;

    // Default constructor for mock data
    public BluetoothDeviceViewModel()
    {
        _device = null;
        TrackingInfoText = "";
        HasTrackingInfo = Visibility.Collapsed;
    }

    public BluetoothDeviceViewModel(BluetoothDeviceInfo device)
    {
        _device = device;
        UpdateTrackingInfo();
    }

    // Properties that can be set for mock data
    private string _name = "";
    public string Name
    {
        get => _device?.Name ?? _name;
        set => SetProperty(ref _name, value);
    }

    private string _id = "";
    public string Id
    {
        get => _device?.Id ?? _id;
        set => SetProperty(ref _id, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _device?.IsConnected ?? _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private int _batteryLevel;
    public int BatteryLevel
    {
        get => _device?.BatteryLevel ?? _batteryLevel;
        set
        {
            if (SetProperty(ref _batteryLevel, value))
            {
                // Always record battery for real devices
                if (_device != null)
                {
                    BluetoothWidget.BatteryTracker.RecordBattery(Id, Name, value);
                    UpdateTrackingInfo();
                }
            }
        }

    private void UpdateTrackingInfo()
    {
        // Always update tracking info for real devices
        if (_device != null)
        {
            var summary = BluetoothWidget.BatteryTracker.GetSummaryText(Id);
            TrackingInfoText = summary;
            HasTrackingInfo = Visibility.Visible;
        }
        else
        {
            TrackingInfoText = "";
            HasTrackingInfo = Visibility.Collapsed;
        }
    }
    }

    private string _timeRemainingText = "";
    public string TimeRemainingText
    {
        get => _timeRemainingText;
        set => SetProperty(ref _timeRemainingText, value);
    }

    private string _trackingInfoText = "";
    public string TrackingInfoText
    {
        get => _trackingInfoText;
        set => SetProperty(ref _trackingInfoText, value);
    }

    private Visibility _hasTrackingInfo = Visibility.Collapsed;
    public Visibility HasTrackingInfo
    {
        get => _hasTrackingInfo;
        set => SetProperty(ref _hasTrackingInfo, value);
    }

    public string BatteryDisplayText
    {
        get
        {
            var level = BatteryLevel;
            if (level > 0)
                return $"{level}%";
            return "—";
        }
    }

    public double BatteryTextOpacity => BatteryLevel > 0 ? 1.0 : 0.5;

    public string ConnectionStatusText => IsConnected ? "Connected" : "Disconnected";

    public SolidColorBrush ConnectionStatusBrush
    {
        get
        {
            if (IsConnected)
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80)); // Green
            return new SolidColorBrush(Windows.UI.Color.FromArgb(128, 128, 128, 128)); // Gray
        }
    }
}
