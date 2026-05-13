using System.Text;

namespace Notey.App.Editing;

public sealed record ObsidianImageEmbed(string VaultRelativePath, string RawEmbed, int LineNumber);

public static class ObsidianImageEmbedIndex
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".webp",
            ".bmp",
            ".svg"
        };

    public static IReadOnlyDictionary<int, ObsidianImageEmbed> Build(string text)
    {
        var embeds = new Dictionary<int, ObsidianImageEmbed>();
        if (string.IsNullOrEmpty(text))
        {
            return embeds;
        }

        using var reader = new StringReader(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));

        var lineNumber = 0;
        var insideFence = false;
        var activeFenceMarker = '\0';
        var activeFenceLength = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
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

                continue;
            }

            if (!insideFence && TryExtractFirstImageEmbed(line, lineNumber, out var embed))
            {
                embeds[lineNumber] = embed;
            }
        }

        return embeds;
    }

    private static bool TryExtractFirstImageEmbed(string line, int lineNumber, out ObsidianImageEmbed embed)
    {
        var insideInlineCode = false;

        for (var index = 0; index < line.Length - 2; index++)
        {
            if (line[index] == '`')
            {
                insideInlineCode = !insideInlineCode;
                continue;
            }

            if (insideInlineCode || !line.AsSpan(index).StartsWith("![[".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            var endIndex = line.IndexOf("]]", index + 3, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                break;
            }

            var rawEmbed = line[index..(endIndex + 2)];
            var inner = line[(index + 3)..endIndex];
            var aliasSeparator = inner.IndexOf('|');
            var embedPath = (aliasSeparator >= 0 ? inner[..aliasSeparator] : inner).Trim();
            if (!IsSupportedImagePath(embedPath))
            {
                index = endIndex + 1;
                continue;
            }

            embed = new ObsidianImageEmbed(NormalizeEmbedPath(embedPath), rawEmbed, lineNumber);
            return true;
        }

        embed = null!;
        return false;
    }

    private static bool TryReadFence(string trimmedLine, out char marker, out int length)
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

    private static bool IsSupportedImagePath(string embedPath)
    {
        if (string.IsNullOrWhiteSpace(embedPath))
        {
            return false;
        }

        return SupportedExtensions.Contains(Path.GetExtension(embedPath.Trim()));
    }

    private static string NormalizeEmbedPath(string embedPath)
    {
        return embedPath
            .Trim()
            .Replace('\\', '/');
    }
}
