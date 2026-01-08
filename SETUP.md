# Developer Setup

This file is for developers who want to build Froggy from source.

**Regular users: Just download the installer from [Releases](https://github.com/shoam321/froggy/releases/latest)**

## Building from Source

1. Install .NET 8.0 SDK
2. Clone this repo
3. Run:
   ```powershell
   dotnet build BluetoothWidget.csproj
   dotnet run --project BluetoothWidget.csproj
   ```

## Creating a Release

```powershell
.\release.ps1 -Version "1.1.0" -Notes "What changed"
```
