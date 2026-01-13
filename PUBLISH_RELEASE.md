Publish a release (no token required for this script)
===============================================

This document explains how to publish a release for the `froggy` project using the GitHub CLI.

Important notes
---------------
- The script uses interactive authentication via `gh auth login --web`. Do NOT paste tokens into chat or files.
- This script does not store or transmit your credentials to any third party. You authenticate locally.

Prerequisites
-------------
- `gh` (GitHub CLI) installed and available on PATH.
- PowerShell (Windows) to run the included script.
- The project built locally (see `bin/Debug/.../Froggy.exe`).

Quick steps
-----------
1. Build the project: open Visual Studio or run `dotnet build` in the `froggy` folder.
2. From the `froggy` folder run the helper script:

```powershell
.\scripts\publish-release.ps1 -Tag "v1.2.1" -Asset ".\bin\Debug\net8.0-windows10.0.19041.0\Froggy.exe" -NotesFile ".\RELEASE_NOTES_v1.2.1.md"
```

What the script does
--------------------
- Creates a ZIP asset (named `Froggy-<tag>-windows.zip`) from the provided asset path.
- Ensures you are authenticated with `gh` (opens the browser for interactive login if needed).
- Creates a GitHub release with the given tag and attaches the ZIP asset.

If you prefer a web UI
---------------------
1. Open the repository Releases page: https://github.com/shoam321/froggy/releases
2. Click "Draft a new release", choose tag `v1.2.1`, paste the release notes from `RELEASE_NOTES_v1.2.1.md`, attach the built asset, and publish.

Security
--------
Never paste GitHub tokens or passwords into chat. The script uses the official `gh` interactive flow so credentials remain local.
