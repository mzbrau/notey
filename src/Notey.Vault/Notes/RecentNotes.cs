namespace Notey.Vault.Notes;

public static class RecentNotes
{
    public static IReadOnlyList<RecentNoteSummary> OrderByMostRecent(
        IEnumerable<RecentNoteSummary> notes,
        int? maxCount = null)
    {
        ArgumentNullException.ThrowIfNull(notes);

        if (maxCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Recent note limit cannot be negative.");
        }

        IEnumerable<RecentNoteSummary> ordered = notes
            .OrderByDescending(static note => note.CreatedAt);

        if (maxCount is { } limit)
        {
            ordered = ordered.Take(limit);
        }

        return ordered.ToArray();
    }
}
