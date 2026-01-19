using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using BluetoothWidget.Services;
using BluetoothWidget.ViewModels;
using WinRT.Interop;

namespace BluetoothWidget;

public sealed partial class MainWindow : Window
{
    private readonly BluetoothService _bluetoothService;
    private readonly ObservableCollection<BluetoothDeviceViewModel> _devices = new();
    private readonly DispatcherTimer _refreshTimer;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _configurationSource;
    private bool _useMockData = false; // Set to false for real Bluetooth data

    public MainWindow()
    {
        this.InitializeComponent();

        // Force dark theme and set up Mica Alt backdrop
        if (Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = ElementTheme.Dark;
        }
        TrySetMicaAltBackdrop();

        // Extend content into title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Set window size
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(800, 500));

        // Initialize services
        _bluetoothService = new BluetoothService();
        DevicesGrid.ItemsSource = _devices;

        // Set up auto-refresh timer (every 30 seconds)
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _refreshTimer.Tick += async (s, e) => await RefreshDevicesAsync();
        _refreshTimer.Start();

        // Initial load
        Activated += MainWindow_Activated;
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= MainWindow_Activated;
        await RefreshDevicesAsync();
    }

    private bool TrySetMicaAltBackdrop()
    {
        if (!MicaController.IsSupported())
            return false;

        _configurationSource = new SystemBackdropConfiguration();
        
        // Force dark theme for backdrop
        _configurationSource.Theme = SystemBackdropTheme.Dark;

        _micaController = new MicaController { Kind = MicaKind.BaseAlt };
        _micaController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        _micaController.SetSystemBackdropConfiguration(_configurationSource);

        return true;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        LoadingRing.IsActive = true;
        EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            _devices.Clear();

            if (_useMockData)
            {
                // Load mock data for design preview
                LoadMockDevices();
            }
            else
            {
                // Load real Bluetooth devices
                var devices = await _bluetoothService.GetBluetoothDevicesAsync();
                foreach (var device in devices)
                {
                    _devices.Add(new BluetoothDeviceViewModel(device));
                }
            }

            // Update device count
            DeviceCountText.Text = _devices.Count == 1 ? "1 device" : $"{_devices.Count} devices";

            if (_devices.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing devices: {ex}");
            EmptyState.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private void LoadMockDevices()
    {
        // Mock devices for design preview
        _devices.Add(new BluetoothDeviceViewModel
        {
            Name = "WF-1000XM5",
            IsConnected = true,
            BatteryLevel = 75,
            TimeRemainingText = "~4h 30m left",
            TrackingInfoText = "📊 Tracking for 2h 15m",
            HasTrackingInfo = Visibility.Visible
        });

        _devices.Add(new BluetoothDeviceViewModel
        {
            Name = "AirPods Pro",
            IsConnected = true,
            BatteryLevel = 42,
            TimeRemainingText = "~2h 10m left",
            TrackingInfoText = "📊 Tracking for 45m",
            HasTrackingInfo = Visibility.Visible
        });

        _devices.Add(new BluetoothDeviceViewModel
        {
            Name = "MX Master 3",
            IsConnected = true,
            BatteryLevel = 15,
            TimeRemainingText = "Low battery!",
            TrackingInfoText = "",
            HasTrackingInfo = Visibility.Collapsed
        });

        _devices.Add(new BluetoothDeviceViewModel
        {
            Name = "Galaxy Buds2",
            IsConnected = false,
            BatteryLevel = 0,
            TimeRemainingText = "",
            TrackingInfoText = "",
            HasTrackingInfo = Visibility.Collapsed
        });
    }

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Scale = new System.Numerics.Vector3(1.03f, 1.03f, 1f);
            border.Translation = new System.Numerics.Vector3(0, -2, 12);
        }
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Scale = new System.Numerics.Vector3(1f, 1f, 1f);
            border.Translation = new System.Numerics.Vector3(0, 0, 8);
        }
    }

    private async void DrainageTestButton_Click(object sender, RoutedEventArgs e)
    {
        BluetoothDeviceViewModel.RunDrainageTest();
        // Read the test log and show result
        var wsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "drainage_test_log.txt");
        string result = "Drainage test log not found.";
        if (System.IO.File.Exists(wsPath))
        {
            result = System.IO.File.ReadAllText(wsPath);
        }
        var dialog = new ContentDialog
        {
            Title = "Battery Drainage Test Result",
            Content = result,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }
}
