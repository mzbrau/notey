namespace Notey.Vault.Abstractions;

public sealed record VaultPaths(
    string RootPath,
    string ImagesPath,
    string NotesPath,
    string DraftPath,
    string PeoplePath)
{
    public string MeetingsPath => Path.Combine(NotesPath, "Meetings");
}
