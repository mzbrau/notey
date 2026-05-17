using Notey.Vault.Tasks;

namespace Notey.App.Assistant;

public sealed record NoteyAssistantRequest(
    string Prompt,
    string NoteText,
    int CaretOffset,
    int SelectionStart,
    int SelectionLength,
    string? CurrentNotePath,
    bool IsDraft,
    IReadOnlyList<NoteyTask> Tasks);

public sealed record NoteyAssistantResponse(
    string Message,
    IReadOnlyList<AssistantNoteOperation> NoteOperations,
    IReadOnlyList<AssistantTaskOperation> TaskOperations);

public sealed record NoteyAssistantResult(
    string Message,
    IReadOnlyList<AssistantNoteOperation> NoteOperations,
    IReadOnlyList<AssistantTaskOperation> TaskOperations,
    IReadOnlyList<string> Warnings)
{
    public bool HasChanges => NoteOperations.Count > 0 || TaskOperations.Count > 0;
}

public abstract record AssistantNoteOperation;

public sealed record InsertNoteTextOperation(int Offset, string Text) : AssistantNoteOperation;

public sealed record ReplaceNoteRangeOperation(
    int Start,
    int Length,
    string Text,
    string? ExpectedText) : AssistantNoteOperation;

public sealed record DeleteNoteRangeOperation(
    int Start,
    int Length,
    string? ExpectedText) : AssistantNoteOperation;

public sealed record ReplaceAllNoteTextOperation(
    string Text,
    string? ExpectedText) : AssistantNoteOperation;

public enum AssistantTaskOperationKind
{
    Add,
    Update,
    Remove,
    Complete,
    Reopen,
    SetDueDate
}

public sealed record AssistantTaskOperation(
    AssistantTaskOperationKind Kind,
    string? TaskId,
    string? Text,
    DateOnly? DueDate);
