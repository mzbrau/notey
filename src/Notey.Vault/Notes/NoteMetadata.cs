namespace Notey.Vault.Notes;

public sealed record NoteMetadata(
    IReadOnlyList<string> People,
    IReadOnlyList<string> Topics,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ScreenshotContext)
{
    public static NoteMetadata Empty { get; } = new([], [], [], [], []);

    public bool HasContext =>
        People.Count > 0
        || Topics.Count > 0
        || Projects.Count > 0
        || Tags.Count > 0
        || ScreenshotContext.Count > 0;
}
