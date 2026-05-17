using Notey.Vault.Tasks;

namespace Notey.App.Assistant;

public static class AssistantOperationValidator
{
    public static NoteyAssistantResult Validate(
        NoteyAssistantResponse response,
        string currentNoteText,
        IReadOnlyList<NoteyTask> currentTasks)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(currentNoteText);
        ArgumentNullException.ThrowIfNull(currentTasks);

        var warnings = new List<string>();
        ValidateNoteOperations(response.NoteOperations, currentNoteText, warnings);
        ValidateTaskOperations(response.TaskOperations, currentTasks, warnings);

        return new NoteyAssistantResult(
            response.Message,
            response.NoteOperations,
            response.TaskOperations,
            warnings);
    }

    private static void ValidateNoteOperations(
        IReadOnlyList<AssistantNoteOperation> operations,
        string currentNoteText,
        ICollection<string> warnings)
    {
        var replaceAllCount = operations.Count(static operation => operation is ReplaceAllNoteTextOperation);
        if (replaceAllCount > 1 || (replaceAllCount == 1 && operations.Count > 1))
        {
            warnings.Add("Assistant returned replaceAll together with other note edits; no changes were applied.");
            return;
        }

        var ranges = new List<(int Start, int End)>();
        var inserts = new List<int>();
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case InsertNoteTextOperation insert:
                    if (!IsValidOffset(insert.Offset, currentNoteText.Length) || string.IsNullOrEmpty(insert.Text))
                    {
                        warnings.Add("Assistant returned an invalid insertText note operation.");
                    }

                    inserts.Add(insert.Offset);
                    break;
                case ReplaceNoteRangeOperation replace:
                    if (replace.ExpectedText is null)
                    {
                        warnings.Add("Assistant replaceRange note operation must include expectedText.");
                    }

                    if (TryValidateRange(currentNoteText, replace.Start, replace.Length, replace.ExpectedText, warnings, "replaceRange", out var replaceRange))
                    {
                        ranges.Add(replaceRange);
                    }

                    break;
                case DeleteNoteRangeOperation delete:
                    if (delete.ExpectedText is null)
                    {
                        warnings.Add("Assistant deleteRange note operation must include expectedText.");
                    }

                    if (TryValidateRange(currentNoteText, delete.Start, delete.Length, delete.ExpectedText, warnings, "deleteRange", out var deleteRange))
                    {
                        ranges.Add(deleteRange);
                    }

                    break;
                case ReplaceAllNoteTextOperation replaceAll:
                    if (replaceAll.ExpectedText is null)
                    {
                        warnings.Add("Assistant replaceAll note operation must include expectedText.");
                    }

                    if (replaceAll.ExpectedText is not null
                        && !string.Equals(currentNoteText, replaceAll.ExpectedText, StringComparison.Ordinal))
                    {
                        warnings.Add("Assistant replaceAll expected text did not match the current note.");
                    }

                    break;
            }
        }

        foreach (var pair in ranges.OrderBy(static range => range.Start).Zip(ranges.OrderBy(static range => range.Start).Skip(1)))
        {
            if (pair.First.End > pair.Second.Start)
            {
                warnings.Add("Assistant returned overlapping note edit ranges; no note changes were applied.");
                return;
            }
        }

        foreach (var insertOffset in inserts)
        {
            if (ranges.Any(range => insertOffset >= range.Start && insertOffset < range.End))
            {
                warnings.Add("Assistant returned an insert inside another note edit range; no note changes were applied.");
                return;
            }
        }
    }

    private static void ValidateTaskOperations(
        IReadOnlyList<AssistantTaskOperation> operations,
        IReadOnlyList<NoteyTask> currentTasks,
        ICollection<string> warnings)
    {
        var taskIds = currentTasks.Select(static task => task.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operations)
        {
            if (operation.Kind == AssistantTaskOperationKind.Add)
            {
                if (string.IsNullOrWhiteSpace(operation.Text))
                {
                    warnings.Add("Assistant returned an add task operation without task text.");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(operation.TaskId) || !taskIds.Contains(operation.TaskId))
            {
                warnings.Add($"Assistant referenced missing task id '{operation.TaskId ?? "<empty>"}'.");
                continue;
            }

            if (!referencedTaskIds.Add(operation.TaskId))
            {
                warnings.Add($"Assistant returned multiple operations for task '{operation.TaskId}'.");
            }

            if (operation.Kind == AssistantTaskOperationKind.Update && string.IsNullOrWhiteSpace(operation.Text))
            {
                warnings.Add($"Assistant returned an update for task '{operation.TaskId}' without task text.");
            }

            if (operation.Kind == AssistantTaskOperationKind.SetDueDate && operation.DueDate is null)
            {
                warnings.Add($"Assistant returned setDueDate for task '{operation.TaskId}' without a due date.");
            }
        }
    }

    private static bool TryValidateRange(
        string currentNoteText,
        int start,
        int length,
        string? expectedText,
        ICollection<string> warnings,
        string operationName,
        out (int Start, int End) range)
    {
        range = default;
        if (start < 0 || length < 0 || start > currentNoteText.Length)
        {
            warnings.Add($"Assistant returned an out-of-range {operationName} note operation.");
            return false;
        }

        var end = (long)start + length;
        if (end > currentNoteText.Length)
        {
            warnings.Add($"Assistant returned an out-of-range {operationName} note operation.");
            return false;
        }

        range = (start, (int)end);
        if (expectedText is not null
            && !string.Equals(currentNoteText.Substring(start, length), expectedText, StringComparison.Ordinal))
        {
            warnings.Add($"Assistant {operationName} expected text did not match the current note.");
        }

        return true;
    }

    private static bool IsValidOffset(int offset, int length)
    {
        return offset >= 0 && offset <= length;
    }
}
