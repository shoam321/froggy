@echo off
echo ========================================
echo  FROG TECH Installer Builder
echo ========================================
echo.

REM Check if Inno Setup is installed
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set ISCC="C:\Program Files\Inno Setup 6\ISCC.exe"
) else (
    echo ERROR: Inno Setup 6 not found!
    echo.
    echo Please download and install Inno Setup from:
    echo https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo Step 1: Building Release version...
cd /d "%~dp0.."
dotnet publish BluetoothWidget.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

if errorlevel 1 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Step 2: Creating installer...
cd /d "%~dp0"
%ISCC% setup.iss

if errorlevel 1 (
    echo Installer creation failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo  SUCCESS! Installer created in:
echo  Installer\Output\FrogTech_Setup_1.0.0.exe
echo ========================================
echo.
pause
