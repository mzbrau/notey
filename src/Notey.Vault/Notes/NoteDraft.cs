namespace Notey.Vault.Notes;

public sealed record NoteDraft(string FilePath, string Content, DateTimeOffset CreatedAt);
