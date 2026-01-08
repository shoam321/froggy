# FROG TECH Installer Guide

## Requirements

1. **Inno Setup 6** - Download free from: https://jrsoftware.org/isdl.php

## Creating the Installer

### Option 1: Use the batch file
1. Install Inno Setup 6
2. Double-click `build_installer.bat`
3. Find the installer in `Installer\Output\`

### Option 2: Manual
1. Build the app: `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish`
2. Open `Installer\setup.iss` in Inno Setup
3. Press Ctrl+F9 to compile

## Files Included in Installer

- `BluetoothWidget.exe` - Main application
- `Assets/logo.jpg` - App logo
- `Assets/animation.mp4` - Intro video

## Installer Features

- ✅ Desktop shortcut (optional)
- ✅ Start with Windows (optional)
- ✅ Start Menu entry
- ✅ Clean uninstaller
- ✅ Modern wizard style

## Video During Installation

The current setup shows a placeholder. For actual video playback during install, you have options:

### Option A: Show video on first app launch (Recommended)
The app can play `animation.mp4` as a splash screen the first time it runs.

### Option B: Use Inno Setup video plugins
- Download VCL Media Player plugin
- Or use ShellExecute to open video in default player

## Icon File

For the .exe icon and taskbar, you need a `.ico` file:
1. Convert `logo.jpg` to `.ico` at https://convertio.co/jpg-ico/
2. Save as `Assets\logo.ico`
3. Rebuild the installer
