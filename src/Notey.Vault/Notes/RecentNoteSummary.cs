namespace Notey.Vault.Notes;

public sealed record RecentNoteSummary(string FilePath, DateTimeOffset CreatedAt, string Title)
{
    public string SearchText { get; init; } = string.Empty;
}
