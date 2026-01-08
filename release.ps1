# ============================================
#  FROGGY RELEASE SCRIPT
#  by Shoam
# ============================================
#
#  HOW TO USE:
#  -----------
#  1. Make your code changes (fix bugs, add features, etc.)
#  2. Save all your files in VS Code (Ctrl+S)
#  3. Open PowerShell in the BT folder
#  4. Run this script with version and notes:
#
#     .\release.ps1 -Version "1.1.0" -Notes "Fixed loading animation, added pin button"
#
#  WHAT IT DOES:
#  -------------
#  - Updates version number in App.xaml.cs
#  - Updates version number in setup.iss (installer)
#  - Updates update.xml (for auto-updates)
#  - Builds and publishes Froggy.exe
#  - Compiles the installer
#  - Commits and pushes all changes to GitHub
#  - Creates a new GitHub release with the installer
#
#  VERSIONING:
#  -----------
#  Use semantic versioning: MAJOR.MINOR.PATCH
#  - MAJOR: Big changes, breaking updates (2.0.0)
#  - MINOR: New features (1.1.0, 1.2.0)
#  - PATCH: Bug fixes (1.0.1, 1.0.2)
#
# ============================================

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$Notes = "Bug fixes and improvements"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Froggy Release Script v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Update version in App.xaml.cs
Write-Host "[1/6] Updating version in App.xaml.cs..." -ForegroundColor Yellow
$appFile = "c:\Users\shoam\Desktop\BT\App.xaml.cs"
$content = Get-Content $appFile -Raw
$content = $content -replace 'CurrentVersion = "[^"]*"', "CurrentVersion = `"$Version`""
Set-Content $appFile $content
Write-Host "       Done!" -ForegroundColor Green

# 2. Update version in setup.iss
Write-Host "[2/6] Updating version in setup.iss..." -ForegroundColor Yellow
$issFile = "c:\Users\shoam\Desktop\BT\Installer\setup.iss"
$content = Get-Content $issFile -Raw
$content = $content -replace 'AppVersion=[\d.]+', "AppVersion=$Version"
$content = $content -replace 'OutputBaseFilename=Froggy_Setup_[\d.]+', "OutputBaseFilename=Froggy_Setup_$Version"
Set-Content $issFile $content
Write-Host "       Done!" -ForegroundColor Green

# 3. Update update.xml
Write-Host "[3/6] Updating update.xml..." -ForegroundColor Yellow
$updateXml = @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>$Version</version>
    <url>https://github.com/shoam321/froggy/releases/download/v$Version/Froggy_Setup_$Version.exe</url>
    <changelog>https://github.com/shoam321/froggy/releases/tag/v$Version</changelog>
    <mandatory>false</mandatory>
</item>
"@
Set-Content "c:\Users\shoam\Desktop\BT\update.xml" $updateXml
Write-Host "       Done!" -ForegroundColor Green

# 4. Build and publish
Write-Host "[4/6] Building and publishing..." -ForegroundColor Yellow
Get-Process Froggy -ErrorAction SilentlyContinue | Stop-Process -Force
Set-Location "c:\Users\shoam\Desktop\BT"
dotnet publish BluetoothWidget.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
if ($LASTEXITCODE -ne 0) { throw "Build failed!" }
Write-Host "       Done!" -ForegroundColor Green

# 5. Compile installer
Write-Host "[5/6] Compiling installer..." -ForegroundColor Yellow
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "c:\Users\shoam\Desktop\BT\Installer\setup.iss"
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed!" }
Write-Host "       Done!" -ForegroundColor Green

# 6. Push to GitHub and create release
Write-Host "[6/6] Pushing to GitHub and creating release..." -ForegroundColor Yellow
git add -A
git commit -m "Release v$Version"
git push

$installerPath = "c:\Users\shoam\Desktop\BT\Installer\Output\Froggy_Setup_$Version.exe"
gh release create "v$Version" $installerPath --title "Froggy v$Version" --notes $Notes
Write-Host "       Done!" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Release v$Version complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Release URL: https://github.com/shoam321/froggy/releases/tag/v$Version" -ForegroundColor White
