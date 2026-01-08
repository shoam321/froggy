#  Froggy

A sleek Windows desktop widget that monitors your Bluetooth devices battery levels in real-time.

## Features

- Real-time battery monitoring for Bluetooth headphones, mice, keyboards
- Battery drain rate tracking - see how fast your battery is draining
- Time remaining estimates - know when to charge
- Network speed monitor - download/upload speeds and ping
- 4 beautiful themes - Retro, Pixel, NeonDrift, Moss
- Pin on top button - keep it visible or let it go behind windows
- Screen edge snapping - smash it to screen corners
- Auto-updates - get notified when new versions are available
- Rolling champion quotes - Leona and Kayle wisdom

## Download

**[Download Latest Release](https://github.com/shoam321/froggy/releases/latest)**

Just run the installer - no additional setup required!

## Usage

- **Drag** anywhere to move the window
- **Pin button** to keep on top of other windows
- **Theme button** to switch between 4 themes
- **A? button** to change text size (S/M/L)
- **Refresh button** to manually refresh
- **Right-click** a device to rename it

## Building from Source

```powershell
git clone https://github.com/shoam321/froggy.git
cd froggy
dotnet build BluetoothWidget.csproj
dotnet run --project BluetoothWidget.csproj
```

### Releasing a new version

```powershell
.\release.ps1 -Version "1.1.0" -Notes "What changed"
```

## Requirements

- Windows 10/11
- .NET 8.0 Runtime (installer will prompt if needed)
- Bluetooth adapter

## Troubleshooting

No devices showing?
1. Make sure Bluetooth is enabled in Windows Settings
2. Devices must be paired AND connected
3. Try the refresh button
4. Some devices dont report battery levels

---
Made by Shoam
