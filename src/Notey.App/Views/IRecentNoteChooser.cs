using Avalonia.Controls;
using Notey.Vault.Notes;

namespace Notey.App.Views;

public interface IRecentNoteChooser
{
    Task<RecentNoteChoice> ChooseAsync(Window owner, IReadOnlyList<RecentNoteSummary> recentNotes);
}

