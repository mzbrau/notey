using Notey.Vault.Notes;

namespace Notey.App.Views;

public enum RecentNoteChoiceAction
{
    Cancel,
    OpenExisting,
    NewNote
}

public sealed record RecentNoteChoice(RecentNoteChoiceAction Action, RecentNoteSummary? SelectedNote)
{
    public static RecentNoteChoice Cancel { get; } = new(RecentNoteChoiceAction.Cancel, null);

    public static RecentNoteChoice NewNote { get; } = new(RecentNoteChoiceAction.NewNote, null);

    public static RecentNoteChoice Open(RecentNoteSummary note) => new(RecentNoteChoiceAction.OpenExisting, note);
}
