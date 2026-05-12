---
id: windows-publishing
title: Windows publishing
sidebar_position: 1
---

Phase 11 provides a Windows folder publish rather than a full installer/update system. This keeps packaging reproducible while installer choices remain open.

## Publish profile

The app includes `src/Notey.App/Properties/PublishProfiles/win-x64-folder.pubxml`.

Important settings:

- `RuntimeIdentifier`: `win-x64`
- `SelfContained`: `true`
- `PublishSingleFile`: `true`
- `PublishReadyToRun`: `true`
- `PublishTrimmed`: `false`

Trimming is disabled because desktop UI frameworks and configuration binding frequently rely on reflection.

## Publish script

```powershell
./scripts/publish-windows.ps1
```

The script writes output to `artifacts/publish/Notey-win-x64` unless another output path is provided.

## CI workflow

The GitHub Actions workflow builds and tests on Windows. Manual `workflow_dispatch` runs also publish and upload a `Notey-win-x64` artifact.
