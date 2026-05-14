using Notey.Vault.Notes;

namespace Notey.Tests;

public sealed class RecentNotesTests
{
    [Fact]
    public void OrderByMostRecent_sorts_descending()
    {
        var older = new RecentNoteSummary("/vault/Notes/older.md", new DateTimeOffset(2026, 5, 12, 9, 0, 0, TimeSpan.Zero), "Older");
        var newest = new RecentNoteSummary("/vault/Notes/newest.md", new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.Zero), "Newest");
        var middle = new RecentNoteSummary("/vault/Notes/middle.md", new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero), "Middle");

        var ordered = RecentNotes.OrderByMostRecent([older, newest, middle]);

        Assert.Collection(
            ordered,
            item => Assert.Equal(newest.FilePath, item.FilePath),
            item => Assert.Equal(middle.FilePath, item.FilePath),
            item => Assert.Equal(older.FilePath, item.FilePath));
    }

    [Fact]
    public void OrderByMostRecent_applies_max_count_after_sorting()
    {
        var oldest = new RecentNoteSummary("/vault/Notes/oldest.md", new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero), "Oldest");
        var newer = new RecentNoteSummary("/vault/Notes/newer.md", new DateTimeOffset(2026, 5, 13, 9, 0, 0, TimeSpan.Zero), "Newer");
        var newest = new RecentNoteSummary("/vault/Notes/newest.md", new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.Zero), "Newest");

        var ordered = RecentNotes.OrderByMostRecent([oldest, newer, newest], maxCount: 2);

        Assert.Collection(
            ordered,
            item => Assert.Equal(newest.FilePath, item.FilePath),
            item => Assert.Equal(newer.FilePath, item.FilePath));
    }
}
