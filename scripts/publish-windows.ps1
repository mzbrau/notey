param(
    [string] $Configuration = "Release",
    [string] $RuntimeIdentifier = "win-x64",
    [string] $OutputPath = "artifacts/publish/Notey-win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/Notey.App/Notey.App.csproj"
$publishProfile = Join-Path $repoRoot "src/Notey.App/Properties/PublishProfiles/win-x64-folder.pubxml"
$publishPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $repoRoot $OutputPath
}

dotnet publish $projectPath `
    --configuration $Configuration `
    -p:PublishProfile="$publishProfile" `
    -p:RuntimeIdentifier=$RuntimeIdentifier `
    --output $publishPath

Write-Host "Notey published to $publishPath"
