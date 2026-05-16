namespace Notey.Core.Notes;

public sealed record SlashCommandCompletionQuery(int ReplacementStart, int ReplacementLength, string SearchText)
{
    public static SlashCommandCompletionQuery? TryCreate(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (caretOffset < 0 || caretOffset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(caretOffset), "Caret offset must be within the text bounds.");
        }

        var lineStart = SlashCommandQueryText.FindLineStart(text, caretOffset);
        var beforeCaret = text[lineStart..caretOffset];
        var leadingWhitespace = beforeCaret.Length - beforeCaret.TrimStart().Length;
        var slashIndex = lineStart + leadingWhitespace;
        if (slashIndex >= text.Length || slashIndex >= caretOffset || text[slashIndex] != '/')
        {
            return null;
        }

        var searchStart = slashIndex + 1;
        var searchText = text[searchStart..caretOffset];
        if (searchText.Any(static character => char.IsWhiteSpace(character) || !IsCommandNameCharacter(character)))
        {
            return null;
        }

        return new SlashCommandCompletionQuery(slashIndex, caretOffset - slashIndex, searchText);
    }

    private static bool IsCommandNameCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '-' or '_';
    }
}

public sealed record SlashCommandParameterQuery(
    int ReplacementStart,
    int ReplacementLength,
    string CommandName,
    string SearchText)
{
    public static SlashCommandParameterQuery? TryCreate(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (caretOffset < 0 || caretOffset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(caretOffset), "Caret offset must be within the text bounds.");
        }

        var lineStart = SlashCommandQueryText.FindLineStart(text, caretOffset);
        var lineUntilCaret = text[lineStart..caretOffset];
        var trimmedStartLength = lineUntilCaret.Length - lineUntilCaret.TrimStart().Length;
        if (trimmedStartLength >= lineUntilCaret.Length || lineUntilCaret[trimmedStartLength] != '/')
        {
            return null;
        }

        var commandStart = trimmedStartLength + 1;
        var whitespace = lineUntilCaret.IndexOfAny([' ', '\t'], commandStart);
        if (whitespace < 0)
        {
            return null;
        }

        var commandName = lineUntilCaret[commandStart..whitespace];
        if (commandName.Length == 0 || commandName.Any(static character => !IsCommandNameCharacter(character)))
        {
            return null;
        }

        var parameterStartInLine = whitespace + 1;
        var parameter = lineUntilCaret[parameterStartInLine..];
        return new SlashCommandParameterQuery(
            lineStart + parameterStartInLine,
            caretOffset - (lineStart + parameterStartInLine),
            commandName,
            parameter.TrimStart());
    }

    public bool IsTaskDueDateQuery => string.Equals(CommandName, "task", StringComparison.OrdinalIgnoreCase)
        && SearchText.Contains("//", StringComparison.Ordinal);

    private static bool IsCommandNameCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '-' or '_';
    }

}

internal static class SlashCommandQueryText
{
    public static int FindLineStart(string text, int caretOffset)
    {
        if (caretOffset == 0)
        {
            return 0;
        }

        var lineStart = text.LastIndexOf('\n', caretOffset - 1);
        return lineStart < 0 ? 0 : lineStart + 1;
    }
}
