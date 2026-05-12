namespace Notey.Vault.Abstractions;

public sealed record VaultPaths(
    string NotesPath,
    string PeoplePath,
    string TopicsPath,
    string ProjectsPath,
    string ScreenshotPath)
{
    public string RootPath { get; init; } = string.Empty;
}
