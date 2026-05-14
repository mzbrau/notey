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
}
