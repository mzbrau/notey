namespace Notey.Core.Notes;

public sealed record PersonReferenceCompletionQuery(int ReplacementStart, int ReplacementLength, string SearchText)
{
    public static PersonReferenceCompletionQuery? TryCreate(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (caretOffset < 0 || caretOffset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(caretOffset), "Caret offset must be within the text bounds.");
        }

        var lineStart = text.LastIndexOf('\n', Math.Max(0, caretOffset - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var atIndex = text.LastIndexOf('@', Math.Max(0, caretOffset - 1), caretOffset - lineStart);
        if (atIndex < lineStart)
        {
            return null;
        }

        if (atIndex > 0 && IsWordCharacter(text[atIndex - 1]))
        {
            return null;
        }

        var searchText = text[(atIndex + 1)..caretOffset];
        if (searchText.Any(static character => !IsSearchCharacter(character)))
        {
            return null;
        }

        return new PersonReferenceCompletionQuery(atIndex, caretOffset - atIndex, searchText.TrimStart());
    }

    private static bool IsSearchCharacter(char character)
    {
        return char.IsLetterOrDigit(character)
            || char.IsWhiteSpace(character)
            || character is '\'' or '-' or '_' or '.';
    }

    private static bool IsWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }
}
