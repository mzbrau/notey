namespace Notey.Core.Notes;

public sealed record MarkdownTextEdit(
    int ReplacementStart,
    int ReplacementLength,
    string ReplacementText,
    int SelectionStart,
    int SelectionLength,
    int CaretOffset);
