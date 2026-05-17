namespace Notey.Vault.Notes;

public interface INoteDraftStore
{
    Task<NoteDraft> CreateAsync(DateTimeOffset createdAt, CancellationToken cancellationToken = default);

    Task<NoteDraft> OpenAsync(string filePath, CancellationToken cancellationToken = default);

    Task<NoteDraft?> FindMostRecentAsync(DateTimeOffset createdAfter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentNoteSummary>> ListRecentAsync(DateTimeOffset createdAfter, CancellationToken cancellationToken = default);

    Task SaveAsync(NoteDraft draft, string content, CancellationToken cancellationToken = default);

    Task DeleteEmptyDraftsAsync(CancellationToken cancellationToken = default);
}
