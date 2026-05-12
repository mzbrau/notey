using System.Text.Json;
using System.Text.Json.Nodes;
using Notey.Pipelines.Data;

namespace Notey.PipelineSteps;

public static class AiStructuredResponseParser
{
    public static StructuredNoteData Parse(string response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);

        var json = ExtractJsonObject(response);
        JsonNode root;
        try
        {
            root = JsonNode.Parse(json) ?? throw new JsonException("Root JSON value was null.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("AI structured extraction response did not contain valid JSON.", ex);
        }

        return new StructuredNoteData(
            Summary: ReadString(root, "summary"),
            MeetingTitle: ReadString(root, "meetingTitle") ?? ReadString(root, "meeting_title"),
            People: ReadEntities(root["people"], "person"),
            Topics: ReadEntities(root["topics"], "topic"),
            Projects: ReadEntities(root["projects"], "project"),
            Tags: ReadStringArray(root["tags"]),
            Sections: ReadSections(root["sections"]));
    }

    public static string ExtractJsonObject(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = StripMarkdownFence(trimmed);
        }

        var start = trimmed.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            throw new FormatException("AI structured extraction response did not contain a JSON object.");
        }

        var depth = 0;
        var inString = false;
        var escaping = false;
        for (var index = start; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (character == '\\' && inString)
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return trimmed[start..(index + 1)];
                }
            }
        }

        throw new FormatException("AI structured extraction response contained an incomplete JSON object.");
    }

    private static string StripMarkdownFence(string response)
    {
        var lines = response.Split('\n', StringSplitOptions.TrimEntries);
        if (lines.Length >= 2 && lines[^1].StartsWith("```", StringComparison.Ordinal))
        {
            return string.Join('\n', lines[1..^1]).Trim();
        }

        return response;
    }

    private static string? ReadString(JsonNode? node, string key)
    {
        return node?[key]?.GetValue<string>()?.Trim();
    }

    private static IReadOnlyList<EntitySuggestion> ReadEntities(JsonNode? node, string kind)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(item => ReadEntity(item, kind))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .ToArray();
    }

    private static EntitySuggestion? ReadEntity(JsonNode? node, string kind)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : new EntitySuggestion(text.Trim(), kind);
        }

        var name = ReadString(node, "name") ?? ReadString(node, "title");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        double? confidence = node["confidence"] is JsonValue confidenceValue
            && confidenceValue.TryGetValue<double>(out var parsedConfidence)
                ? parsedConfidence
                : null;
        var source = ReadString(node, "source");

        return new EntitySuggestion(name.Trim(), kind, confidence, source);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(static item => item?.GetValue<string>()?.Trim())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadSections(JsonNode? node)
    {
        if (node is not JsonObject sections)
        {
            return new Dictionary<string, string>();
        }

        return sections
            .Where(static property => !string.IsNullOrWhiteSpace(property.Key) && property.Value is not null)
            .Select(static property => new KeyValuePair<string, string>(property.Key.Trim(), property.Value!.ToString().Trim()))
            .Where(static property => !string.IsNullOrWhiteSpace(property.Value))
            .ToDictionary(static property => property.Key, static property => property.Value, StringComparer.OrdinalIgnoreCase);
    }
}
