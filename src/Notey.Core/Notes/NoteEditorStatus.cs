using System.Text.RegularExpressions;

namespace Notey.Core.Notes;

public sealed partial record NoteEditorStatus(int WordCount, int Line, int Column)
{
    public static NoteEditorStatus FromText(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (caretOffset < 0 || caretOffset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(caretOffset), "Caret offset must be within the note text.");
        }

        var line = 1;
        var lastLineStart = 0;

        for (var i = 0; i < caretOffset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastLineStart = i + 1;
            }
        }

        return new NoteEditorStatus(WordRegex().Count(text), line, caretOffset - lastLineStart + 1);
    }

    [GeneratedRegex(@"[\p{L}\p{N}_]+(?:['’-][\p{L}\p{N}_]+)?")]
    private static partial Regex WordRegex();
}
