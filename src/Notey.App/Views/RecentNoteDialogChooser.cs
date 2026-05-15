using Avalonia.Controls;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public sealed class RecentNoteDialogChooser : IRecentNoteChooser
{
    public Task<RecentNoteChoice> ChooseAsync(Window owner, IReadOnlyList<RecentNoteSummary> recentNotes)
    {
        return RecentNoteChoiceWindow.ShowAsync(owner, recentNotes);
    }
}

