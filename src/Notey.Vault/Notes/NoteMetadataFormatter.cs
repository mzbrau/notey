using System.Text;

namespace Notey.Vault.Notes;

public sealed class NoteMetadataFormatter
{
    private const string ContextStartMarker = "<!-- notey-context:start -->";
    private const string ContextEndMarker = "<!-- notey-context:end -->";

    public string Apply(string markdown, NoteMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(metadata);

        var normalizedMarkdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var (frontmatter, body) = SplitFrontmatter(normalizedMarkdown);
        var updatedFrontmatter = UpdateFrontmatter(frontmatter, metadata);
        var updatedBody = UpdateContextBlock(body, metadata);

        return $"{updatedFrontmatter}\n{updatedBody}";
    }

    public static (IReadOnlyList<string> People, IReadOnlyList<string> Topics, IReadOnlyList<string> Projects, IReadOnlyList<string> Tags, IReadOnlyList<string> ScreenshotContext) ReadFrontmatterInputs(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        if (lines.Length < 3 || lines[0] != "---")
        {
            return ([], [], [], [], []);
        }

        var endLine = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i] == "---")
            {
                endLine = i;
                break;
            }
        }

        if (endLine < 0)
        {
            return ([], [], [], [], []);
        }

        return (
            ReadFrontmatterArray(lines, 1, endLine, "people"),
            ReadFrontmatterArray(lines, 1, endLine, "topics"),
            ReadFrontmatterArray(lines, 1, endLine, "projects"),
            ReadFrontmatterArray(lines, 1, endLine, "tags"),
            ReadFrontmatterArray(lines, 1, endLine, "screenshot_context"));
    }

    private static IReadOnlyList<string> ReadFrontmatterArray(string[] lines, int startLine, int endLine, string key)
    {
        for (var i = startLine; i < endLine; i++)
        {
            var separator = lines[i].IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var lineKey = lines[i][..separator].Trim();
            if (!string.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = lines[i][(separator + 1)..].Trim();

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                return ParseInlineFrontmatterArray(value);
            }

            if (string.IsNullOrEmpty(value))
            {
                return ParseBlockFrontmatterArray(lines, i + 1, endLine);
            }
        }

        return [];
    }

    private static IReadOnlyList<string> ParseInlineFrontmatterArray(string value)
    {
        var inner = value.Trim('[', ']');
        if (string.IsNullOrWhiteSpace(inner))
        {
            return [];
        }

        return inner
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(UnquoteFrontmatterValue)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static IReadOnlyList<string> ParseBlockFrontmatterArray(string[] lines, int startIndex, int endLine)
    {
        var values = new List<string>();
        for (var i = startIndex; i < endLine; i++)
        {
            if (!lines[i].StartsWith("  - ", StringComparison.Ordinal))
            {
                break;
            }

            values.Add(UnquoteFrontmatterValue(lines[i][4..].Trim()));
        }

        return values;
    }

    private static string UnquoteFrontmatterValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static (string Frontmatter, string Body) SplitFrontmatter(string markdown)
    {
        if (!markdown.StartsWith("---\n", StringComparison.Ordinal))
        {
            return ("---\n---", markdown);
        }

        var endIndex = markdown.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return ("---\n---", markdown);
        }

        var endOfFrontmatter = endIndex + "\n---".Length;
        return (markdown[..endOfFrontmatter], markdown[endOfFrontmatter..].TrimStart('\n'));
    }

    private static string UpdateFrontmatter(string frontmatter, NoteMetadata metadata)
    {
        var lines = frontmatter.Split('\n');
        var output = new List<string> { "---" };

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line == "---")
            {
                continue;
            }

            if (IsManagedKey(line))
            {
                index = SkipManagedBlock(lines, index);
                continue;
            }

            output.Add(line);
        }

        AppendYamlArray(output, "people", metadata.People);
        AppendYamlArray(output, "topics", metadata.Topics);
        AppendYamlArray(output, "projects", metadata.Projects);
        AppendYamlArray(output, "tags", metadata.Tags);
        AppendYamlArray(output, "screenshot_context", metadata.ScreenshotContext);
        output.Add("---");

        return string.Join('\n', output);
    }

    private static string UpdateContextBlock(string body, NoteMetadata metadata)
    {
        var cleanedBody = RemoveExistingContextBlock(body).TrimStart('\n');
        if (!metadata.HasContext)
        {
            return cleanedBody;
        }

        var builder = new StringBuilder();
        builder.AppendLine(ContextStartMarker);
        builder.AppendLine("## Context");
        AppendContextLine(builder, "People", metadata.People);
        AppendContextLine(builder, "Topics", metadata.Topics);
        AppendContextLine(builder, "Projects", metadata.Projects);
        AppendContextLine(builder, "Tags", metadata.Tags);
        AppendContextLine(builder, "Screenshot context", metadata.ScreenshotContext);
        builder.AppendLine(ContextEndMarker);
        builder.AppendLine();
        builder.Append(cleanedBody);

        return builder.ToString();
    }

    private static string RemoveExistingContextBlock(string body)
    {
        var start = body.IndexOf(ContextStartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return body;
        }

        var end = body.IndexOf(ContextEndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return RemoveOrphanedContextBlock(body, start);
        }

        end += ContextEndMarker.Length;
        return body.Remove(start, end - start).TrimStart('\n');
    }

    private static string RemoveOrphanedContextBlock(string body, int start)
    {
        var cursor = MoveToNextLine(body, start);

        if (LineEquals(body, cursor, "## Context"))
        {
            cursor = MoveToNextLine(body, cursor);
        }

        while (LineStartsWith(body, cursor, "- "))
        {
            cursor = MoveToNextLine(body, cursor);
        }

        if (cursor < body.Length && body[cursor] == '\n')
        {
            cursor++;
        }

        return body.Remove(start, cursor - start).TrimStart('\n');
    }

    private static int MoveToNextLine(string text, int index)
    {
        var nextLineIndex = text.IndexOf('\n', index);
        return nextLineIndex < 0 ? text.Length : nextLineIndex + 1;
    }

    private static bool LineEquals(string text, int index, string expected)
    {
        return string.Equals(ReadLine(text, index), expected, StringComparison.Ordinal);
    }

    private static bool LineStartsWith(string text, int index, string prefix)
    {
        return ReadLine(text, index).StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string ReadLine(string text, int index)
    {
        if (index >= text.Length)
        {
            return string.Empty;
        }

        var nextLineIndex = text.IndexOf('\n', index);
        return nextLineIndex < 0 ? text[index..] : text[index..nextLineIndex];
    }

    private static bool IsManagedKey(string line)
    {
        var separator = line.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        var key = line[..separator].Trim();
        return key is "people" or "topics" or "projects" or "tags" or "screenshot_context";
    }

    private static int SkipManagedBlock(IReadOnlyList<string> lines, int index)
    {
        while (index + 1 < lines.Count && lines[index + 1].StartsWith("  - ", StringComparison.Ordinal))
        {
            index++;
        }

        return index;
    }

    private static void AppendYamlArray(ICollection<string> output, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            output.Add($"{key}: []");
            return;
        }

        output.Add($"{key}:");
        foreach (var value in values)
        {
            output.Add($"  - \"{EscapeYaml(value)}\"");
        }
    }

    private static void AppendContextLine(StringBuilder builder, string label, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
        {
            builder.Append("- ").Append(label).Append(": ").AppendLine(string.Join(", ", values));
        }
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
