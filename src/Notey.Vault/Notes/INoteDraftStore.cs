namespace Notey.Vault.Notes;

public interface INoteDraftStore
{
    Task<NoteDraft> CreateAsync(DateTimeOffset createdAt, CancellationToken cancellationToken = default);

    Task SaveAsync(NoteDraft draft, string content, CancellationToken cancellationToken = default);
}
