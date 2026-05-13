---
id: installation
title: Installation
sidebar_position: 1
---

Notey currently publishes as a Windows folder build rather than a full installer. The folder publish is the safest first packaging target because it keeps the application, Avalonia dependencies, OCR configuration files, and JSON pipeline definitions together without adding installer-specific behavior.

## Requirements

- Windows for tray icon, global hotkey, and screen snipping.
- .NET 10 SDK for development builds.
- Tesseract OCR installed and available on `PATH`, or configured with a full executable path.
- An Obsidian vault folder or another markdown folder for saved notes.
- Optional OpenAI-compatible API endpoint for AI screenshot and note organization pipelines.

## Build from source

```bash
dotnet build Notey.slnx
dotnet test Notey.slnx --no-build --logger "console;verbosity=minimal"
```

## Publish for Windows

```powershell
./scripts/publish-windows.ps1
```

The default output folder is:

```text
artifacts/publish/Notey-win-x64
```

Run `Notey.exe` from that folder.
