---
id: installation
title: Installation
sidebar_position: 1
---

Notey publishes Windows installer assets from GitHub Releases. Release versions come from `v*` Git tags, for example `v0.2.0`.

## Requirements

- Windows for tray icon, global hotkey, and screen snipping.
- .NET 10 SDK for development builds.
- Tesseract OCR installed and available on `PATH`, or configured with a full executable path.
- An Obsidian vault folder or another markdown folder for saved notes.
- Optional OpenAI-compatible API endpoint for AI note processing.

## Build from source

```bash
dotnet build Notey.slnx
dotnet test Notey.slnx --no-build --logger "console;verbosity=minimal"
```

## Install from a release

Download the latest Windows setup executable from the repository's GitHub Releases page and run it. Release assets are packaged with Velopack and target `win-x64`.

Installers are unsigned initially, so Windows may show a SmartScreen warning until code signing is added.

## Upgrades

To upgrade, download and run the newer setup executable from a later GitHub Release. Release/feed metadata is published with each release so Notey can add in-app update checks later, but the app does not currently prompt for updates automatically.

## Publish for Windows

```powershell
./scripts/publish-windows.ps1
```

The default output folder is:

```text
artifacts/publish/Notey-win-x64
```

Run `Notey.exe` from that folder.

To create installer assets locally after publishing, install the Velopack CLI and package the publish output:

```powershell
dotnet tool install --global vpk --version 0.0.1298
./scripts/package-windows.ps1 -Version 0.2.0
```
