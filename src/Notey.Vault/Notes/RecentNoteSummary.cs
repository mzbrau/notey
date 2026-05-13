namespace Notey.Vault.Notes;

public sealed record RecentNoteSummary(string FilePath, DateTimeOffset CreatedAt, string Title);
