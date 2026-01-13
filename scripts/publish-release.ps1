param(
    [string]$Tag = "v1.2.1",
    [string]$Asset = ".\bin\Debug\net8.0-windows10.0.19041.0\Froggy.exe",
    [string]$Zip = $(".\Froggy-$Tag-windows.zip"),
    [string]$NotesFile = $(".\RELEASE_NOTES_$Tag.md")
)

Write-Host "Preparing release $Tag"

if (-Not (Test-Path $Asset)) {
    Write-Error "Asset not found: $Asset"
    exit 1
}

# Create ZIP asset
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path $Asset -DestinationPath $Zip -Force

# Ensure gh is available
if (-Not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "GitHub CLI (gh) is not installed. Install it and re-run the script."
    exit 1
}

# Ensure interactive auth (opens browser if needed)
gh auth status 2>$null
if ($LASTEXITCODE -ne 0) {
    gh auth login --web
}

if (-Not (Test-Path $NotesFile)) {
    Write-Warning "Notes file not found: $NotesFile. Creating basic notes."
    "Release $Tag" | Out-File $NotesFile -Encoding utf8
}

Write-Host "Creating release $Tag and uploading $Zip"
gh release create $Tag $Zip --title $Tag --notes-file $NotesFile

if ($LASTEXITCODE -eq 0) { Write-Host "Release $Tag created successfully." } else { Write-Error "Failed to create release." }
