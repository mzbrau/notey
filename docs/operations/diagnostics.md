---
id: diagnostics
title: Diagnostics
sidebar_position: 1
---

Notey includes a diagnostics export command for collecting safe configuration and environment details without exposing API keys.

## Export diagnostics

```powershell
Notey.exe --export-diagnostics C:\Temp\notey-diagnostics.md
```

If no output path is supplied, Notey writes a markdown report under the local application data folder.

## What is included

- App version, OS, architecture, and .NET runtime.
- Vault folder configuration.
- Whether AI provider base URLs, models, and API keys are configured.
- OCR executable and language configuration.
- Pipeline definition file location and validation results.

## What is excluded

- API key values.
- Note contents.
- Screenshot image contents.
- Raw AI responses.
