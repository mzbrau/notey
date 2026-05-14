using Notey.App.Views;
using Notey.Vault.Notes;

namespace Notey.Tests;

public sealed class RecentNoteChoiceTests
{
    [Fact]
    public void CreateRecentNoteCardContent_returns_placeholder_for_null_note()
    {
        var control = RecentNoteChoiceWindow.CreateRecentNoteCardContent(null);

        Assert.NotNull(control);
        Assert.False(control.IsVisible);
    }

    [Fact]
    public void FromDialogResult_returns_cancel_when_dialog_result_is_null()
    {
        var result = RecentNoteChoice.FromDialogResult(null);

        Assert.Same(RecentNoteChoice.Cancel, result);
    }

    [Fact]
    public void FromDialogResult_preserves_a_real_choice()
    {
        var note = new RecentNoteSummary("/vault/Notes/example.md", new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero), "Example");
        var choice = RecentNoteChoice.Open(note);

        var result = RecentNoteChoice.FromDialogResult(choice);

        Assert.Same(choice, result);
    }

    [Fact]
    public void ShouldOpenSelectedNote_requires_selection()
    {
        Assert.True(RecentNoteChoiceWindow.ShouldOpenSelectedNote(hasSelection: true));
        Assert.False(RecentNoteChoiceWindow.ShouldOpenSelectedNote(hasSelection: false));
    }

    [Fact]
    public void FilterRecentNotes_matches_note_contents_and_preserves_order()
    {
        var first = new RecentNoteSummary("/vault/Notes/alpha.md", new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero), "Alpha")
        {
            SearchText = "Alpha\nalpha.md\n/vault/Notes/alpha.md\nshared phrase"
        };
        var second = new RecentNoteSummary("/vault/Notes/beta.md", new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero), "Beta")
        {
            SearchText = "Beta\nbeta.md\n/vault/Notes/beta.md\nshared phrase"
        };

        var filtered = RecentNoteChoiceWindow.FilterRecentNotes([first, second], "shared phrase");

        Assert.Collection(
            filtered,
            item => Assert.Equal(first.FilePath, item.FilePath),
            item => Assert.Equal(second.FilePath, item.FilePath));
    }

    [Fact]
    public void FilterRecentNotes_returns_only_matching_notes()
    {
        var matching = new RecentNoteSummary("/vault/Notes/matching.md", new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero), "Matching")
        {
            SearchText = "tag: urgent"
        };
        var other = new RecentNoteSummary("/vault/Notes/other.md", new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero), "Other")
        {
            SearchText = "tag: someday"
        };

        var filtered = RecentNoteChoiceWindow.FilterRecentNotes([matching, other], "urgent");

        Assert.Single(filtered);
        Assert.Equal(matching.FilePath, filtered[0].FilePath);
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    public void GetPreferredSelectedIndex_defaults_to_first_match_or_none(int filteredCount, int expectedIndex)
    {
        Assert.Equal(expectedIndex, RecentNoteChoiceWindow.GetPreferredSelectedIndex(filteredCount));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ShouldCloseOnDeactivate_requires_activation_and_no_explicit_choice(
        bool hasActivated,
        bool hasDialogResult,
        bool shouldClose)
    {
        Assert.Equal(shouldClose, RecentNoteChoiceWindow.ShouldCloseOnDeactivate(hasActivated, hasDialogResult));
    }
}
