# Bluetooth Widget

A Windows desktop widget that displays connected Bluetooth devices and their battery levels.

## Features

- 📱 Shows all connected Bluetooth devices
- 🔋 Displays battery levels when available
- 🔄 Auto-refreshes every 30 seconds
- 🎨 Modern, dark-themed UI
- 📌 Always on top for easy monitoring
- 🪟 Draggable and resizable window

## Requirements

- Windows 10/11
- .NET 8.0 SDK
- Bluetooth adapter

## Building

```powershell
dotnet restore
dotnet build
```

## Running

```powershell
dotnet run
```

Or build a release version:

```powershell
dotnet publish -c Release -r win-x64 --self-contained
```

## Usage

- **Drag** the window by clicking and dragging anywhere on it
- **Refresh** manually using the 🔄 button
- **Close** using the ✕ button
- The widget automatically scans for devices every 30 seconds

## Notes

- Battery level detection depends on device support and Windows APIs
- Some devices may not report battery levels
- The widget uses both InTheHand.BluetoothLE library and Windows Management Instrumentation (WMI) for device detection

## Troubleshooting

If no devices appear:
1. Ensure Bluetooth is enabled in Windows Settings
2. Make sure devices are paired and connected
3. Try the manual refresh button
4. Check that your Bluetooth adapter is working properly
