---
id: windows-publishing
title: Windows publishing
sidebar_position: 1
---

Notey publishes Windows releases from `v*` Git tags. Release builds use MinVer so assembly and package versions come from the tag, and CI packages the Windows publish output with Velopack.

The release workflow intentionally checks that the tagged commit is reachable from `origin/main`. Git tags are independent refs, so the workflow cannot literally mean "tag pushed to main" without this guard.

## Versioning

Release tags must start with `v` and use a SemVer version:

```text
v0.2.0
v1.0.0
v1.0.0-rc.1
```

MinVer is configured in `Directory.Build.props` with `MinVerTagPrefix` set to `v`. The workflow resolves `MinVerVersion` during the release and fails if it does not match the pushed tag without the `v` prefix.

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

## Installer packaging

The release workflow packages the published folder with Velopack:

```powershell
./scripts/package-windows.ps1 -Version 0.2.0
```

The packaging script expects `vpk` on `PATH`. CI installs it with:

```powershell
dotnet tool install --global vpk --version 0.0.1298
```

Installer and release/feed assets are written to `artifacts/release`. The release/feed metadata is generated now so future in-app update support can reuse the same GitHub Release assets; Notey does not yet check for updates inside the app.

## GitHub release workflow

`.github/workflows/release.yml` runs when a tag matching `v*` is pushed. It:

1. Checks out full history and tags.
2. Fetches `origin/main`.
3. Fails if the tag commit is not reachable from `origin/main`.
4. Restores, builds, and tests the solution in Release configuration.
5. Publishes the `win-x64` folder build.
6. Packages Velopack installer and release/feed assets.
7. Uploads workflow artifacts and publishes the GitHub Release.

If the main-branch guard fails, move the intended commit onto `main`, delete and recreate the tag there, then push the corrected tag.

The normal CI workflow still builds and tests pull requests. Manual `workflow_dispatch` runs can still publish the folder artifact without creating a GitHub Release.
