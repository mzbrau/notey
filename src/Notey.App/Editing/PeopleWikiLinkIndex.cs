namespace Notey.App.Editing;

public sealed record PeopleWikiLinkSpan(int Offset, int Length, string LinkPath, string DisplayText);

public static class PeopleWikiLinkIndex
{
    private const string PeoplePrefix = "People/";

    public static IReadOnlyList<PeopleWikiLinkSpan> Build(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return [];
        }

        var spans = new List<PeopleWikiLinkSpan>();
        var offset = 0;
        var insideFence = false;
        var activeFenceMarker = '\0';
        var activeFenceLength = 0;

        while (offset < text.Length)
        {
            var lineEnd = text.IndexOf('\n', offset);
            var lineLength = lineEnd < 0 ? text.Length - offset : lineEnd - offset;
            var line = text.AsSpan(offset, lineLength).TrimEnd('\r');
            var trimmedLine = line.TrimStart();

            if (TryReadFence(trimmedLine, out var fenceMarker, out var fenceLength))
            {
                if (!insideFence)
                {
                    insideFence = true;
                    activeFenceMarker = fenceMarker;
                    activeFenceLength = fenceLength;
                }
                else if (fenceMarker == activeFenceMarker && fenceLength >= activeFenceLength)
                {
                    insideFence = false;
                }
            }
            else if (!insideFence)
            {
                AddLineLinks(text, offset, line.Length, spans);
            }

            if (lineEnd < 0)
            {
                break;
            }

            offset = lineEnd + 1;
        }

        return spans;
    }

    private static void AddLineLinks(string text, int lineOffset, int lineLength, ICollection<PeopleWikiLinkSpan> spans)
    {
        var lineEnd = lineOffset + lineLength;
        var inlineCodeRunLength = 0;

        for (var offset = lineOffset; offset < lineEnd - 3; offset++)
        {
            if (text[offset] == '`')
            {
                var runLength = CountBacktickRun(text, offset, lineEnd);
                if (inlineCodeRunLength == 0)
                {
                    inlineCodeRunLength = runLength;
                }
                else if (runLength == inlineCodeRunLength)
                {
                    inlineCodeRunLength = 0;
                }

                offset += runLength - 1;
                continue;
            }

            if (inlineCodeRunLength != 0
                || !text.AsSpan(offset).StartsWith("[[".AsSpan(), StringComparison.Ordinal)
                || (offset > 0 && text[offset - 1] == '!'))
            {
                continue;
            }

            var endOffset = text.IndexOf("]]", offset + 2, StringComparison.Ordinal);
            if (endOffset < 0 || endOffset >= lineEnd)
            {
                continue;
            }

            var inner = text[(offset + 2)..endOffset];
            if (inner.Contains("[[", StringComparison.Ordinal))
            {
                continue;
            }

            var aliasSeparator = FindAliasSeparator(inner);
            var linkPath = (aliasSeparator >= 0 ? inner[..aliasSeparator] : inner).Trim();
            if (!IsPeoplePath(linkPath))
            {
                offset = endOffset + 1;
                continue;
            }

            var alias = aliasSeparator >= 0 ? UnescapeAlias(inner[(aliasSeparator + 1)..].Trim()) : string.Empty;
            var displayText = string.IsNullOrWhiteSpace(alias) ? GetFallbackDisplayText(linkPath) : alias;
            if (!string.IsNullOrWhiteSpace(displayText))
            {
                spans.Add(new PeopleWikiLinkSpan(offset, endOffset + 2 - offset, linkPath, displayText));
            }

            offset = endOffset + 1;
        }
    }

    private static bool IsPeoplePath(string linkPath)
    {
        return linkPath.StartsWith(PeoplePrefix, StringComparison.OrdinalIgnoreCase)
            && linkPath.Length > PeoplePrefix.Length;
    }

    private static int FindAliasSeparator(string inner)
    {
        for (var index = 0; index < inner.Length; index++)
        {
            if (inner[index] == '|' && (index == 0 || inner[index - 1] != '\\'))
            {
                return index;
            }
        }

        return -1;
    }

    private static string UnescapeAlias(string alias)
    {
        return alias.Replace("\\|", "|", StringComparison.Ordinal);
    }

    private static string GetFallbackDisplayText(string linkPath)
    {
        var pathWithoutSubtarget = linkPath.Split(['#', '^'], 2)[0].TrimEnd('/');
        var slashIndex = pathWithoutSubtarget.LastIndexOf('/');
        return slashIndex >= 0 ? pathWithoutSubtarget[(slashIndex + 1)..] : pathWithoutSubtarget;
    }

    private static int CountBacktickRun(string text, int offset, int lineEnd)
    {
        var length = 0;
        while (offset + length < lineEnd && text[offset + length] == '`')
        {
            length++;
        }

        return length;
    }

    private static bool TryReadFence(ReadOnlySpan<char> trimmedLine, out char marker, out int length)
    {
        marker = '\0';
        length = 0;

        if (trimmedLine.Length < 3)
        {
            return false;
        }

        marker = trimmedLine[0];
        if (marker is not ('`' or '~'))
        {
            marker = '\0';
            return false;
        }

        while (length < trimmedLine.Length && trimmedLine[length] == marker)
        {
            length++;
        }

        return length >= 3;
    }
}
