using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using BluetoothWidget.Services;

namespace BluetoothWidget.ViewModels;

public partial class BluetoothDeviceViewModel : ObservableObject
{
    private readonly BluetoothDeviceInfo? _device;

    // Default constructor for mock data
    public BluetoothDeviceViewModel()
    {
        _device = null;
    }

    public BluetoothDeviceViewModel(BluetoothDeviceInfo device)
    {
        _device = device;
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
        set => SetProperty(ref _batteryLevel, value);
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
