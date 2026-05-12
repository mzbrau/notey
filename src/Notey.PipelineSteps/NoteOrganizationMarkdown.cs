using Notey.Pipelines.Data;

namespace Notey.PipelineSteps;

public static class NoteOrganizationMarkdown
{
    public const string CleanupStartMarker = "<!-- notey-ai-cleanup:start -->";
    public const string CleanupEndMarker = "<!-- notey-ai-cleanup:end -->";

    private const string ContextStartMarker = "<!-- notey-context:start -->";
    private const string ContextEndMarker = "<!-- notey-context:end -->";

    public static string BuildOrganizationInput(
        string markdown,
        IReadOnlyList<string> people,
        IReadOnlyList<string> topics,
        IReadOnlyList<string> projects,
        IReadOnlyList<string> tags)
        => BuildOrganizationInput(markdown, people, topics, projects, tags, out _);

    public static string BuildOrganizationInput(
        string markdown,
        IReadOnlyList<string> people,
        IReadOnlyList<string> topics,
        IReadOnlyList<string> projects,
        IReadOnlyList<string> tags,
        out string userAuthoredBody)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(tags);

        userAuthoredBody = ExtractUserAuthoredBody(markdown);
        var lines = new List<string>();

        lines.Add("Current editable metadata:");
        AppendMetadataLine(lines, "People", people);
        AppendMetadataLine(lines, "Topics", topics);
        AppendMetadataLine(lines, "Projects", projects);
        AppendMetadataLine(lines, "Tags", tags.Select(static tag => $"#{tag.Trim().TrimStart('#')}").ToArray());
        lines.Add(string.Empty);
        lines.Add("User-authored note text:");
        lines.Add(userAuthoredBody);

        return string.Join('\n', lines).Trim();
    }

    public static string ExtractUserAuthoredBody(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var withoutFrontmatter = RemoveFrontmatter(normalized);
        var withoutContext = RemoveMarkedBlock(withoutFrontmatter, ContextStartMarker, ContextEndMarker);
        var withoutCleanup = RemoveMarkedBlock(withoutContext, CleanupStartMarker, CleanupEndMarker);
        return withoutCleanup.Trim();
    }

    public static string ReplaceCleanupBlock(string markdown, string cleanupBlock)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(cleanupBlock);

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var existingStart = normalized.IndexOf(CleanupStartMarker, StringComparison.Ordinal);
        if (existingStart >= 0)
        {
            var existingEnd = normalized.IndexOf(CleanupEndMarker, existingStart, StringComparison.Ordinal);
            var removeEnd = existingEnd < 0
                ? normalized.Length
                : existingEnd + CleanupEndMarker.Length;
            return normalized.Remove(existingStart, removeEnd - existingStart)
                .Insert(existingStart, cleanupBlock.Trim())
                .TrimEnd()
                + "\n";
        }

        var separator = normalized.Length == 0
            ? string.Empty
            : normalized.EndsWith("\n\n", StringComparison.Ordinal)
                ? string.Empty
                : normalized.EndsWith('\n')
                    ? "\n"
                    : "\n\n";
        return $"{normalized}{separator}{cleanupBlock.Trim()}\n";
    }

    public static string RenderCleanupBlock(StructuredNoteData data, string heading)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(heading);

        var content = new List<string>();
        if (!string.IsNullOrWhiteSpace(data.Summary))
        {
            content.Add(data.Summary.Trim());
        }

        if (data.Sections is not null)
        {
            foreach (var (sectionHeading, body) in data.Sections)
            {
                if (string.IsNullOrWhiteSpace(sectionHeading) || string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                content.Add(string.Empty);
                content.Add($"### {sectionHeading.Trim()}");
                content.Add(body.Trim());
            }
        }

        if (content.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            CleanupStartMarker,
            $"## {heading.Trim()}",
            string.Empty,
        };
        lines.AddRange(content);
        lines.Add(CleanupEndMarker);

        return string.Join('\n', lines).Trim();
    }

    private static void AppendMetadataLine(ICollection<string> lines, string label, IReadOnlyList<string> values)
    {
        lines.Add(values.Count == 0
            ? $"- {label}: none"
            : $"- {label}: {string.Join(", ", values)}");
    }

    private static string RemoveFrontmatter(string markdown)
    {
        if (!markdown.StartsWith("---\n", StringComparison.Ordinal))
        {
            return markdown;
        }

        var endIndex = markdown.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return markdown;
        }

        return markdown[(endIndex + "\n---".Length)..].TrimStart('\n');
    }

    private static string RemoveMarkedBlock(string markdown, string startMarker, string endMarker)
    {
        var result = markdown;
        while (true)
        {
            var start = result.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return result;
            }

            var end = result.IndexOf(endMarker, start, StringComparison.Ordinal);
            var removeEnd = end < 0
                ? result.Length
                : end + endMarker.Length;
            result = result.Remove(start, removeEnd - start).TrimStart('\n');
        }
    }
}
