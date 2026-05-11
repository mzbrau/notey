using System.Text.RegularExpressions;

namespace Notey.Core.Notes;

public static partial class MarkdownEditorCommands
{
    public static MarkdownTextEdit ToggleBold(string text, int selectionStart, int selectionLength)
    {
        return ToggleWrap(text, selectionStart, selectionLength, "**");
    }

    public static MarkdownTextEdit ToggleItalic(string text, int selectionStart, int selectionLength)
    {
        return ToggleWrap(text, selectionStart, selectionLength, "_");
    }

    public static MarkdownTextEdit? TryCreateListContinuation(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateRange(text, caretOffset, 0);

        if (caretOffset == 0)
        {
            return null;
        }

        var lineStart = text.LastIndexOf('\n', Math.Max(0, caretOffset - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var linePrefix = text[lineStart..caretOffset].TrimEnd('\r');

        var emptyListMatch = EmptyListItemRegex().Match(linePrefix);
        if (emptyListMatch.Success)
        {
            return new MarkdownTextEdit(lineStart, caretOffset - lineStart, string.Empty, lineStart, 0, lineStart);
        }

        var unorderedMatch = UnorderedListItemRegex().Match(linePrefix);
        if (unorderedMatch.Success)
        {
            var continuation = "\n" + unorderedMatch.Groups["indent"].Value + unorderedMatch.Groups["marker"].Value + " ";
            var nextOffset = caretOffset + continuation.Length;

            return new MarkdownTextEdit(caretOffset, 0, continuation, nextOffset, 0, nextOffset);
        }

        var orderedMatch = OrderedListItemRegex().Match(linePrefix);
        if (orderedMatch.Success)
        {
            if (!int.TryParse(orderedMatch.Groups["number"].Value, out var currentNumber) || currentNumber == int.MaxValue)
            {
                return null;
            }

            var nextNumber = currentNumber + 1;
            var continuation = "\n" + orderedMatch.Groups["indent"].Value + nextNumber + orderedMatch.Groups["separator"].Value + " ";
            var nextOffset = caretOffset + continuation.Length;

            return new MarkdownTextEdit(caretOffset, 0, continuation, nextOffset, 0, nextOffset);
        }

        return null;
    }

    private static MarkdownTextEdit ToggleWrap(string text, int selectionStart, int selectionLength, string marker)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateRange(text, selectionStart, selectionLength);

        if (selectionLength == 0)
        {
            var replacement = marker + marker;
            var caretOffset = selectionStart + marker.Length;

            return new MarkdownTextEdit(selectionStart, 0, replacement, caretOffset, 0, caretOffset);
        }

        var selectedText = text.Substring(selectionStart, selectionLength);
        if (selectedText.StartsWith(marker, StringComparison.Ordinal)
            && selectedText.EndsWith(marker, StringComparison.Ordinal)
            && selectedText.Length >= marker.Length * 2)
        {
            var unwrapped = selectedText[marker.Length..^marker.Length];

            return new MarkdownTextEdit(selectionStart, selectionLength, unwrapped, selectionStart, unwrapped.Length, selectionStart + unwrapped.Length);
        }

        if (HasMarkersAroundSelection(text, selectionStart, selectionLength, marker))
        {
            var replacementStart = selectionStart - marker.Length;
            var replacementLength = selectionLength + marker.Length * 2;

            return new MarkdownTextEdit(replacementStart, replacementLength, selectedText, replacementStart, selectionLength, replacementStart + selectionLength);
        }

        var wrapped = marker + selectedText + marker;
        var innerStart = selectionStart + marker.Length;

        return new MarkdownTextEdit(selectionStart, selectionLength, wrapped, innerStart, selectionLength, innerStart + selectionLength);
    }

    private static bool HasMarkersAroundSelection(string text, int selectionStart, int selectionLength, string marker)
    {
        return selectionStart >= marker.Length
            && selectionStart + selectionLength + marker.Length <= text.Length
            && text.AsSpan(selectionStart - marker.Length, marker.Length).SequenceEqual(marker)
            && text.AsSpan(selectionStart + selectionLength, marker.Length).SequenceEqual(marker);
    }

    private static void ValidateRange(string text, int selectionStart, int selectionLength)
    {
        if (selectionStart < 0 || selectionLength < 0 || selectionStart + selectionLength > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(selectionStart), "Selection must be within the markdown text.");
        }
    }

    [GeneratedRegex(@"^(?<indent>\s*)(?<marker>[-*+])\s+\S.*$")]
    private static partial Regex UnorderedListItemRegex();

    [GeneratedRegex(@"^(?<indent>\s*)(?<number>\d+)(?<separator>[.)])\s+\S.*$")]
    private static partial Regex OrderedListItemRegex();

    [GeneratedRegex(@"^(?<indent>\s*)([-*+]|\d+[.)])\s*$")]
    private static partial Regex EmptyListItemRegex();
}
