using System.Globalization;

namespace Notey.Core.Notes;

public sealed class NoteDirectiveParser
{
    public ParsedNoteDirectives Parse(string markdown, IEnumerable<string> dynamicCommands)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(dynamicCommands);

        var knownDynamicCommands = dynamicCommands
            .Select(static command => command.Trim().TrimStart('/'))
            .Where(static command => command.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var bodyLines = new List<string>(lines.Length);
        var tasks = new List<NoteTaskDirective>();
        var dynamic = new List<DynamicNoteDirective>();
        var unknown = new List<UnknownNoteDirective>();
        var isMeeting = false;
        string? parsedTopic = null;
        ParsedTopicTarget? topicTarget = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!TryParseCommandLine(line, out var commandName, out var parameter))
            {
                bodyLines.Add(line);
                continue;
            }

            if (string.Equals(commandName, "meeting", StringComparison.OrdinalIgnoreCase))
            {
                isMeeting = true;
                continue;
            }

            if (string.Equals(commandName, "topic", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = ParseTopicParameter(parameter);
                if (!string.IsNullOrWhiteSpace(parsed.Title))
                {
                    parsedTopic = parsed.Title;
                    topicTarget = parsed.Target;
                    continue;
                }

                unknown.Add(new UnknownNoteDirective(commandName, parameter, index + 1));
                bodyLines.Add(line);
                continue;
            }

            if (string.Equals(commandName, "task", StringComparison.OrdinalIgnoreCase))
            {
                var task = ParseTask(parameter);
                if (!string.IsNullOrWhiteSpace(task.Text))
                {
                    tasks.Add(task);
                    continue;
                }

                unknown.Add(new UnknownNoteDirective(commandName, parameter, index + 1));
                bodyLines.Add(line);
                continue;
            }

            if (knownDynamicCommands.Contains(commandName))
            {
                if (!string.IsNullOrWhiteSpace(parameter))
                {
                    dynamic.Add(new DynamicNoteDirective(commandName, NormalizeParameter(parameter), index + 1));
                    continue;
                }

                unknown.Add(new UnknownNoteDirective(commandName, parameter, index + 1));
                bodyLines.Add(line);
                continue;
            }

            unknown.Add(new UnknownNoteDirective(commandName, parameter, index + 1));
            bodyLines.Add(line);
        }

        return new ParsedNoteDirectives(
            isMeeting,
            parsedTopic,
            topicTarget,
            tasks,
            dynamic,
            unknown,
            NormalizeBody(bodyLines));
    }

    private static (string Title, ParsedTopicTarget? Target) ParseTopicParameter(string parameter)
    {
        var marker = parameter.IndexOf(" @ ", StringComparison.Ordinal);
        if (marker < 0)
        {
            return (NormalizeParameter(parameter), null);
        }

        var title = NormalizeParameter(parameter[..marker]);
        var relativePath = parameter[(marker + 3)..].Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(relativePath))
        {
            return (NormalizeParameter(parameter), null);
        }

        var normalizedPath = NormalizeTopicTargetPath(relativePath);
        return (title, new ParsedTopicTarget(normalizedPath.Path, normalizedPath.Kind));
    }

    private static (string Path, TopicTargetKind Kind) NormalizeTopicTargetPath(string relativePath)
    {
        var normalized = relativePath.Trim().Replace('\\', '/');
        var kind = TopicTargetKind.Unknown;
        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            kind = TopicTargetKind.Folder;
            normalized = normalized.TrimEnd('/');
        }
        else if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            kind = TopicTargetKind.File;
        }

        return (normalized, kind);
    }

    private static bool TryParseCommandLine(string line, out string commandName, out string parameter)
    {
        commandName = string.Empty;
        parameter = string.Empty;

        var trimmedStart = line.TrimStart();
        if (!trimmedStart.StartsWith('/'))
        {
            return false;
        }

        var content = trimmedStart[1..];
        if (content.Length == 0 || char.IsWhiteSpace(content[0]))
        {
            return false;
        }

        var separator = content.IndexOfAny([' ', '\t']);
        commandName = separator < 0 ? content : content[..separator];
        if (commandName.Any(static character => !IsCommandNameCharacter(character)))
        {
            return false;
        }

        parameter = separator < 0 ? string.Empty : content[(separator + 1)..].Trim();
        return true;
    }

    private static NoteTaskDirective ParseTask(string parameter)
    {
        var parts = parameter.Split(["//"], 2, StringSplitOptions.None);
        var text = NormalizeParameter(parts[0]);
        DateOnly? dueDate = null;
        if (parts.Length == 2)
        {
            var dueDateText = parts[1].Trim();
            if (DateOnly.TryParse(dueDateText, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
                || DateOnly.TryParse(dueDateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                dueDate = parsed;
            }
        }

        return new NoteTaskDirective(text, dueDate);
    }

    private static string NormalizeParameter(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeBody(IReadOnlyList<string> lines)
    {
        return string.Join('\n', lines).Trim();
    }

    private static bool IsCommandNameCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '-' or '_';
    }
}

public sealed record ParsedNoteDirectives(
    bool IsMeeting,
    string? Topic,
    ParsedTopicTarget? TopicTarget,
    IReadOnlyList<NoteTaskDirective> Tasks,
    IReadOnlyList<DynamicNoteDirective> DynamicDirectives,
    IReadOnlyList<UnknownNoteDirective> UnknownDirectives,
    string Body);

public sealed record ParsedTopicTarget(string RelativePath, TopicTargetKind Kind);

public enum TopicTargetKind
{
    Unknown,
    File,
    Folder
}

public sealed record NoteTaskDirective(string Text, DateOnly? DueDate);

public sealed record DynamicNoteDirective(string CommandName, string Value, int LineNumber);

public sealed record UnknownNoteDirective(string CommandName, string Parameter, int LineNumber);
