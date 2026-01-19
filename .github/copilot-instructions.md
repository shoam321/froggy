# Copilot Instructions for Froggy (BluetoothWidget)

## Project Overview
- **Froggy** is a Windows desktop widget for real-time Bluetooth device battery monitoring, network speed, and theming.
- Built with **.NET 8.0** (WPF/WinUI), targeting Windows 10/11.
- Main app logic is in `BluetoothWidget.csproj` and `MainWindow.xaml(.cs)`.
- Themes are defined in `Themes/` (XAML files), assets in `Assets/`.
- Bluetooth and network helpers: `WindowsBluetoothHelper.cs`, `NetworkSpeedHelper.cs`.
- WinUI port lives in `WinUI/` (experimental or alternate UI).

## Architecture & Patterns
- **MVVM-lite**: ViewModels (e.g., `BluetoothDeviceViewModel.cs`) are used for device state, but not strict MVVM everywhere.
- **Helpers**: Device/network logic is separated into helper classes.
- **Controls/**: Custom WPF controls (e.g., `CssPixelArtControl.cs`).
- **Auto-update**: Update logic in `update.xml` and related scripts.
- **Installer**: Uses Inno Setup (`Installer/`), batch and ISS scripts for packaging.

## Build & Run
- Standard build: `dotnet build BluetoothWidget.csproj`
- Run: `dotnet run --project BluetoothWidget.csproj`
- Release: `.\release.ps1 -Version "1.1.0" -Notes "What changed"`
- Installer: Use `Installer/build_installer.bat` (requires Inno Setup 6)

## Key Conventions
- **Themes**: Add new themes in `Themes/`, update theme switch logic in main window.
- **Assets**: Place images, sounds, and fonts in `Assets/`.
- **Device Settings**: Managed via `DeviceSettings.cs`.
- **Quotes**: Champion quotes logic in main window or helper.
- **Pin/Refresh/Theme Buttons**: UI logic in `MainWindow.xaml(.cs)`.

## Integration Points
- **Bluetooth**: Uses Windows APIs via `WindowsBluetoothHelper.cs`.
- **Network**: Speed/ping via `NetworkSpeedHelper.cs`.
- **Installer**: Inno Setup scripts (`setup.iss`), batch automation.
- **Auto-update**: Checks `update.xml` for new releases.

## Examples
- To add a new device property, update `BluetoothDeviceViewModel.cs` and relevant UI bindings.
- To add a theme, create a new XAML in `Themes/` and update theme switch logic.
- To change installer behavior, edit `Installer/setup.iss` and/or `build_installer.bat`.

## Troubleshooting
- If Bluetooth devices don't show, check Windows settings, device connection, and refresh logic.
- For installer issues, verify Inno Setup 6 is installed and batch script paths are correct.

---
**Feedback:** If any section is unclear or missing, please specify what needs improvement or what additional context is needed.
