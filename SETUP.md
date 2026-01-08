# Setup Instructions

## Option 1: Install .NET SDK (Recommended)

1. **Download .NET 8.0 SDK**
   - Visit: https://dotnet.microsoft.com/download/dotnet/8.0
   - Download and install the SDK for Windows

2. **Restore and Build**
   ```powershell
   cd "c:\Users\shoam\Desktop\BT"
   dotnet restore
   dotnet build
   ```

3. **Run the Widget**
   ```powershell
   dotnet run
   ```

## Option 2: Use Visual Studio

1. **Install Visual Studio 2022** (Community Edition is free)
   - Download from: https://visualstudio.microsoft.com/downloads/
   - During installation, select ".NET desktop development" workload

2. **Open the Project**
   - Double-click `BluetoothWidget.csproj` or
   - Open Visual Studio → Open Project → Select `BluetoothWidget.csproj`

3. **Build and Run**
   - Press F5 or click the "Start" button

## Quick Start After Installation

Once .NET SDK is installed, run:

```powershell
cd "c:\Users\shoam\Desktop\BT"
dotnet run
```

## Publishing a Standalone Executable

To create an .exe that doesn't require .NET runtime:

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The executable will be in: `bin\Release\net8.0-windows\win-x64\publish\BluetoothWidget.exe`

## Troubleshooting

### "No .NET SDKs were found"
- Install .NET 8.0 SDK from the link above
- Restart your terminal/PowerShell after installation

### "Bluetooth not available"
- Enable Bluetooth in Windows Settings
- Ensure your PC has a Bluetooth adapter
- Check Device Manager for Bluetooth adapters

### "No devices found"
- Pair devices first through Windows Settings
- Make sure devices are turned on and connected
- Try the refresh button
