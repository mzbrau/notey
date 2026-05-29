using System.Text.RegularExpressions;

namespace Notey.App.Editing.Spellcheck;

public static partial class MarkdownSpellcheckIndex
{
    public static IReadOnlyList<SpellcheckSpan> Build(string text, ISpellcheckService spellcheckService)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(spellcheckService);

        if (!spellcheckService.IsAvailable || text.Length == 0)
        {
            return [];
        }

        var spans = new List<SpellcheckSpan>();
        var inFencedCodeBlock = false;
        foreach (var line in EnumerateLines(text))
        {
            var lineText = text.Substring(line.Offset, line.Length);
            if (FenceRegex().IsMatch(lineText))
            {
                inFencedCodeBlock = !inFencedCodeBlock;
                continue;
            }

            if (!inFencedCodeBlock)
            {
                AddMisspellings(lineText, line.Offset, spellcheckService, spans);
            }

        }

        return spans;
    }

    public static SpellcheckSpan? FindSpanAtOffset(IReadOnlyList<SpellcheckSpan> spans, int offset)
    {
        ArgumentNullException.ThrowIfNull(spans);

        return spans.FirstOrDefault(span => span.Contains(offset));
    }

    private static void AddMisspellings(
        string lineText,
        int lineOffset,
        ISpellcheckService spellcheckService,
        ICollection<SpellcheckSpan> spans)
    {
        var skippedRanges = BuildSkippedRanges(lineText);
        foreach (Match match in WordRegex().Matches(lineText))
        {
            if (IsInSkippedRange(match.Index, match.Length, skippedRanges)
                || ShouldSkipWord(match.Value)
                || spellcheckService.IsCorrect(match.Value))
            {
                continue;
            }

            spans.Add(new SpellcheckSpan(lineOffset + match.Index, match.Length, match.Value));
        }
    }

    private static IReadOnlyList<RangeSpan> BuildSkippedRanges(string lineText)
    {
        var ranges = new List<RangeSpan>();
        AddMatches(InlineCodeRegex(), lineText, ranges);
        AddMatches(WikiLinkRegex(), lineText, ranges);
        AddMatches(MarkdownLinkRegex(), lineText, ranges);
        AddMatches(UrlRegex(), lineText, ranges);
        AddMatches(EmailRegex(), lineText, ranges);
        AddMatches(TopicRegex(), lineText, ranges);
        AddMatches(ImageEmbedRegex(), lineText, ranges);
        AddMatches(DirectiveRegex(), lineText, ranges);
        return ranges;
    }

    private static void AddMatches(Regex regex, string lineText, ICollection<RangeSpan> ranges)
    {
        foreach (Match match in regex.Matches(lineText))
        {
            ranges.Add(new RangeSpan(match.Index, match.Length));
        }
    }

    private static bool IsInSkippedRange(int offset, int length, IEnumerable<RangeSpan> skippedRanges)
    {
        var endOffset = offset + length;
        return skippedRanges.Any(range => offset < range.EndOffset && endOffset > range.Offset);
    }

    private static bool ShouldSkipWord(string word)
    {
        return word.Length <= 1
            || word.Any(char.IsDigit)
            || word.All(character => !char.IsLetter(character) || char.IsUpper(character));
    }

    private static IEnumerable<LineSpan> EnumerateLines(string text)
    {
        var lineStart = 0;
        while (lineStart < text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                yield return new LineSpan(lineStart, text.Length - lineStart, text.Length);
                yield break;
            }

            yield return new LineSpan(lineStart, lineEnd - lineStart, lineEnd + 1);
            lineStart = lineEnd + 1;
        }
    }

    private readonly record struct LineSpan(int Offset, int Length, int EndOffset);

    private readonly record struct RangeSpan(int Offset, int Length)
    {
        public int EndOffset => Offset + Length;
    }

    [GeneratedRegex(@"^\s*(```|~~~)")]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"`[^`]*`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\[\[[^\]]+\]\]")]
    private static partial Regex WikiLinkRegex();

    [GeneratedRegex(@"!?\[[^\]]*\]\([^)]+\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"https?://\S+|www\.\S+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.[\p{L}]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\w)#[\p{L}\p{N}_/-]+")]
    private static partial Regex TopicRegex();

    [GeneratedRegex(@"!\[\[[^\]]+\]\]")]
    private static partial Regex ImageEmbedRegex();

    [GeneratedRegex(@"^\s*/[A-Za-z][\w/-]*")]
    private static partial Regex DirectiveRegex();

    [GeneratedRegex(@"\p{L}+(?:['’-]\p{L}+)*")]
    private static partial Regex WordRegex();
}
