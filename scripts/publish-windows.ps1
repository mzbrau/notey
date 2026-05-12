param(
    [string] $Configuration = "Release",
    [string] $RuntimeIdentifier = "win-x64",
    [string] $OutputPath = "artifacts/publish/Notey-win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/Notey.App/Notey.App.csproj"
$publishPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $repoRoot $OutputPath
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    --output $publishPath

Write-Host "Notey published to $publishPath"
