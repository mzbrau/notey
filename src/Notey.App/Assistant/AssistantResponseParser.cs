using System.Text.Json;
using System.Text.Json.Nodes;

namespace Notey.App.Assistant;

public static class AssistantResponseParser
{
    public static NoteyAssistantResponse Parse(string response)
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
            throw new FormatException("Assistant response did not contain valid JSON.", ex);
        }

        var message = ReadTrimmedString(root, "message") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new FormatException("Assistant response must include a message.");
        }

        return new NoteyAssistantResponse(
            message.Trim(),
            ReadNoteOperations(root["noteOperations"] ?? root["note_operations"]),
            ReadTaskOperations(root["taskOperations"] ?? root["task_operations"]));
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
            throw new FormatException("Assistant response did not contain a JSON object.");
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

        throw new FormatException("Assistant response contained an incomplete JSON object.");
    }

    private static IReadOnlyList<AssistantNoteOperation> ReadNoteOperations(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var operations = new List<AssistantNoteOperation>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var type = ReadTrimmedString(item, "type") ?? ReadTrimmedString(item, "operation");
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new FormatException("Note operation must include a type.");
            }

            operations.Add(type.Trim() switch
            {
                "insertText" or "insert" => new InsertNoteTextOperation(
                    ReadRequiredInt(item, "offset"),
                    ReadRequiredRawString(item, "text")),
                "replaceRange" or "replace" or "update" => new ReplaceNoteRangeOperation(
                    ReadRequiredInt(item, "start"),
                    ReadRequiredInt(item, "length"),
                    ReadRequiredRawString(item, "text"),
                    ReadRawString(item, "expectedText") ?? ReadRawString(item, "expected_text")),
                "deleteRange" or "delete" or "remove" => new DeleteNoteRangeOperation(
                    ReadRequiredInt(item, "start"),
                    ReadRequiredInt(item, "length"),
                    ReadRawString(item, "expectedText") ?? ReadRawString(item, "expected_text")),
                "replaceAll" => new ReplaceAllNoteTextOperation(
                    ReadRequiredRawString(item, "text"),
                    ReadRawString(item, "expectedText") ?? ReadRawString(item, "expected_text")),
                _ => throw new FormatException($"Unsupported note operation type '{type}'.")
            });
        }

        return operations;
    }

    private static IReadOnlyList<AssistantTaskOperation> ReadTaskOperations(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var operations = new List<AssistantTaskOperation>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var type = ReadTrimmedString(item, "type") ?? ReadTrimmedString(item, "operation");
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new FormatException("Task operation must include a type.");
            }

            var kind = type.Trim() switch
            {
                "add" or "addTask" => AssistantTaskOperationKind.Add,
                "update" or "updateTask" => AssistantTaskOperationKind.Update,
                "remove" or "delete" or "deleteTask" => AssistantTaskOperationKind.Remove,
                "complete" or "completeTask" => AssistantTaskOperationKind.Complete,
                "reopen" or "reopenTask" => AssistantTaskOperationKind.Reopen,
                "setDueDate" or "set_due_date" => AssistantTaskOperationKind.SetDueDate,
                _ => throw new FormatException($"Unsupported task operation type '{type}'.")
            };

            operations.Add(new AssistantTaskOperation(
                kind,
                ReadTrimmedString(item, "taskId") ?? ReadTrimmedString(item, "task_id") ?? ReadTrimmedString(item, "id"),
                ReadTrimmedString(item, "text"),
                ReadDate(item["dueDate"] ?? item["due_date"])));
        }

        return operations;
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

    private static string? ReadTrimmedString(JsonNode? node, string key)
    {
        return ReadRawString(node, key)?.Trim();
    }

    private static string? ReadRawString(JsonNode? node, string key)
    {
        if (node?[key] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return text;
    }

    private static string ReadRequiredRawString(JsonNode node, string key)
    {
        var value = ReadRawString(node, key);
        return value is null
            ? throw new FormatException($"Assistant response must include '{key}'.")
            : value;
    }

    private static int ReadRequiredInt(JsonNode node, string key)
    {
        if (node[key] is JsonValue value && value.TryGetValue<int>(out var number))
        {
            return number;
        }

        throw new FormatException($"Assistant response must include integer '{key}'.");
    }

    private static DateOnly? ReadDate(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", out var date)
                ? date
                : throw new FormatException($"Task dueDate '{text}' must use yyyy-MM-dd.");
        }

        return null;
    }
}
