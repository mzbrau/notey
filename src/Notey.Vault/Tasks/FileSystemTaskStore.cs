using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;

namespace Notey.Vault.Tasks;

public sealed partial class FileSystemTaskStore(
    IVaultWorkspace workspace,
    ObsidianLinkBuilder linkBuilder,
    TimeProvider timeProvider) : ITaskStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public string GetTasksFilePath()
    {
        return Path.Combine(workspace.GetPaths().NotesPath, "tasks.md");
    }

    public async Task<IReadOnlyList<NoteyTask>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken);
        return document.Tasks.Select(static task => task.Task).ToArray();
    }

    public async Task<IReadOnlyList<NoteyTask>> AddAsync(
        IReadOnlyList<NewNoteyTask> tasks,
        DateOnly headingDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.Count == 0)
        {
            return [];
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            var existingIds = document.Tasks.Select(static task => task.Task.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var created = new List<NoteyTask>(tasks.Count);

            foreach (var task in tasks)
            {
                var normalizedText = NormalizeTaskText(task.Text);
                if (string.IsNullOrWhiteSpace(normalizedText))
                {
                    continue;
                }

                var id = CreateUniqueId(existingIds);
                existingIds.Add(id);
                created.Add(new NoteyTask(
                    id,
                    normalizedText,
                    task.DueDate,
                    CompletedDate: null,
                    NormalizeSourcePath(task.SourceFilePath)));
            }

            if (created.Count == 0)
            {
                return [];
            }

            AppendTasks(document.Lines, created, headingDate);
            await WriteDocumentAsync(document.Lines, cancellationToken);
            return created;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<NoteyTask?> SetCompletedAsync(
        string taskId,
        DateOnly? completedDate,
        CancellationToken cancellationToken = default)
    {
        return UpdateTaskAsync(
            taskId,
            task => task with { CompletedDate = completedDate },
            cancellationToken);
    }

    public Task<NoteyTask?> SetDueDateAsync(
        string taskId,
        DateOnly? dueDate,
        CancellationToken cancellationToken = default)
    {
        return UpdateTaskAsync(
            taskId,
            task => task with { DueDate = dueDate },
            cancellationToken);
    }

    public Task<NoteyTask?> MoveToThisWeekAsync(
        string taskId,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        return SetDueDateAsync(taskId, today, cancellationToken);
    }

    public async Task RemoveAsync(
        IReadOnlyList<string> taskIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskIds);
        if (taskIds.Count == 0)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            var ids = taskIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var lineIndexes = document.Tasks
                .Where(taskLine => ids.Contains(taskLine.Task.Id))
                .Select(static taskLine => taskLine.LineIndex)
                .OrderDescending()
                .ToArray();
            if (lineIndexes.Length == 0)
            {
                return;
            }

            foreach (var lineIndex in lineIndexes)
            {
                document.Lines.RemoveAt(lineIndex);
            }

            await WriteDocumentAsync(document.Lines, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task AddSourceBacklinksAsync(
        string sourceFilePath,
        IReadOnlyList<NoteyTask> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentNullException.ThrowIfNull(tasks);

        var sourceTasks = tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Id))
            .ToArray();
        if (sourceTasks.Length == 0)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var normalizedSourcePath = Path.GetFullPath(sourceFilePath);
            var content = File.Exists(normalizedSourcePath)
                ? await File.ReadAllTextAsync(normalizedSourcePath, cancellationToken)
                : throw new FileNotFoundException("Task source note was not found.", normalizedSourcePath);
            var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
            var lines = normalized.Split('\n').ToList();
            var insertIndex = FindOrCreateTasksHeading(lines);
            var changed = false;

            foreach (var task in sourceTasks)
            {
                var backlink = $"- {BuildTaskBacklink(task)}";
                if (lines.Any(line => line.Contains($"#^{task.Id}", StringComparison.Ordinal)))
                {
                    continue;
                }

                lines.Insert(insertIndex, backlink);
                insertIndex++;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            await WriteUtf8AtomicallyAsync(normalizedSourcePath, string.Join(newline, lines).TrimEnd() + newline, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<NoteyTask?> UpdateTaskAsync(
        string taskId,
        Func<NoteyTask, NoteyTask> update,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(update);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            var taskLine = document.Tasks.FirstOrDefault(task => string.Equals(task.Task.Id, taskId, StringComparison.OrdinalIgnoreCase));
            if (taskLine is null)
            {
                return null;
            }

            var updated = update(EnsurePersistedId(taskLine.Task, document));
            document.Lines[taskLine.LineIndex] = RenderTaskLine(updated);
            await WriteDocumentAsync(document.Lines, cancellationToken);
            return updated;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static NoteyTask EnsurePersistedId(NoteyTask task, TaskMarkdownDocument document)
    {
        if (!task.Id.StartsWith("legacy-", StringComparison.Ordinal))
        {
            return task;
        }

        var existingIds = document.Tasks
            .Select(static taskLine => taskLine.Task.Id)
            .Where(static id => !id.StartsWith("legacy-", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return task with { Id = CreateUniqueId(existingIds) };
    }

    private async Task<TaskMarkdownDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        var tasksPath = GetTasksFilePath();
        if (!File.Exists(tasksPath))
        {
            return new TaskMarkdownDocument(["# Tasks"], []);
        }

        var content = await File.ReadAllTextAsync(tasksPath, cancellationToken);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return Parse(lines);
    }

    private Task WriteDocumentAsync(List<string> lines, CancellationToken cancellationToken)
    {
        var content = string.Join('\n', lines).TrimEnd() + "\n";
        return WriteUtf8AtomicallyAsync(GetTasksFilePath(), content, cancellationToken);
    }

    private TaskMarkdownDocument Parse(List<string> lines)
    {
        var tasks = new List<TaskMarkdownLine>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (TryParseTask(line, index, out var taskLine))
            {
                tasks.Add(taskLine);
            }
        }

        return new TaskMarkdownDocument(lines, tasks);
    }

    private bool TryParseTask(string line, int lineIndex, out TaskMarkdownLine taskLine)
    {
        taskLine = default!;

        var match = TaskLineRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var body = match.Groups["body"].Value.Trim();
        string? id = null;
        var idMatch = BlockIdRegex().Match(body);
        if (idMatch.Success)
        {
            id = idMatch.Groups["id"].Value;
            body = body[..idMatch.Index].TrimEnd();
        }

        var sourcePath = ReadSourceMetadata(ref body);
        var completedDate = ReadDateMetadata(CompletedDateRegex(), ref body);
        var dueDate = ReadDateMetadata(DueDateRegex(), ref body);
        var text = NormalizeTaskText(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        id ??= CreateLegacyId(lineIndex, line);
        if (completedDate is null && string.Equals(match.Groups["checked"].Value, "x", StringComparison.OrdinalIgnoreCase))
        {
            completedDate = DateOnly.MinValue;
        }

        taskLine = new TaskMarkdownLine(
            new NoteyTask(id, text, dueDate, completedDate, sourcePath),
            lineIndex);
        return true;
    }

    private string? ReadSourceMetadata(ref string body)
    {
        var match = SourceRegex().Match(body);
        if (!match.Success)
        {
            return null;
        }

        body = body.Remove(match.Index, match.Length).Trim();
        var target = match.Groups["target"].Value.Split('|', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var paths = workspace.GetPaths();
        var relativePath = target.Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.GetFullPath(Path.ChangeExtension(Path.Combine(paths.RootPath, relativePath), ".md"));
        return IsInsideVault(paths, filePath) ? filePath : null;
    }

    private static DateOnly? ReadDateMetadata(Regex regex, ref string body)
    {
        var match = regex.Match(body);
        if (!match.Success)
        {
            return null;
        }

        body = body.Remove(match.Index, match.Length).Trim();
        return DateOnly.TryParseExact(
            match.Groups["date"].Value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private void AppendTasks(List<string> lines, IReadOnlyList<NoteyTask> tasks, DateOnly headingDate)
    {
        if (lines.Count == 0)
        {
            lines.Add("# Tasks");
        }

        var heading = $"## {headingDate:yyyy-MM-dd}";
        var insertIndex = FindHeadingInsertIndex(lines, heading);
        foreach (var task in tasks)
        {
            lines.Insert(insertIndex, RenderTaskLine(task));
            insertIndex++;
        }
    }

    private static int FindHeadingInsertIndex(List<string> lines, string heading)
    {
        var headingIndex = lines.FindIndex(line => string.Equals(line.TrimEnd(), heading, StringComparison.Ordinal));
        if (headingIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.Add(heading);
            headingIndex = lines.Count - 1;
        }

        var insertIndex = headingIndex + 1;
        while (insertIndex < lines.Count && !lines[insertIndex].StartsWith("## ", StringComparison.Ordinal))
        {
            insertIndex++;
        }

        if (insertIndex > headingIndex + 1 && !string.IsNullOrWhiteSpace(lines[insertIndex - 1]))
        {
            lines.Insert(insertIndex, string.Empty);
        }

        return insertIndex;
    }

    private static int FindOrCreateTasksHeading(List<string> lines)
    {
        var headingIndex = lines.FindIndex(static line => string.Equals(line.Trim(), "## Tasks", StringComparison.OrdinalIgnoreCase));
        if (headingIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.Add("## Tasks");
            return lines.Count;
        }

        var insertIndex = headingIndex + 1;
        while (insertIndex < lines.Count && !lines[insertIndex].StartsWith("## ", StringComparison.Ordinal))
        {
            insertIndex++;
        }

        return insertIndex;
    }

    private string BuildTaskBacklink(NoteyTask task)
    {
        var tasksLinkPath = linkBuilder.GetLinkPath(workspace.GetPaths(), GetTasksFilePath());
        return ObsidianLinkBuilder.FormatWikiLink($"{tasksLinkPath}#^{task.Id}", $"Task: {task.Text}");
    }

    private string RenderTaskLine(NoteyTask task)
    {
        var builder = new StringBuilder();
        builder.Append("- [").Append(task.IsCompleted ? 'x' : ' ').Append("] ");
        builder.Append(NormalizeTaskText(task.Text));
        if (task.DueDate is { } dueDate)
        {
            builder.Append(" (due: ").Append(dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')');
        }

        if (task.CompletedDate is { } completedDate && completedDate != DateOnly.MinValue)
        {
            builder.Append(" (completed: ").Append(completedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(task.SourceFilePath))
        {
            builder.Append(" (source: ").Append(FormatSourceLink(task.SourceFilePath)).Append(')');
        }

        builder.Append(" ^").Append(task.Id);
        return builder.ToString();
    }

    private string FormatSourceLink(string sourceFilePath)
    {
        var paths = workspace.GetPaths();
        var linkPath = linkBuilder.GetLinkPath(paths, sourceFilePath);
        var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
        return ObsidianLinkBuilder.FormatWikiLink(linkPath, fileName);
    }

    private string? NormalizeSourcePath(string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(sourceFilePath);
        return IsInsideVault(workspace.GetPaths(), fullPath) ? fullPath : null;
    }

    private static bool IsInsideVault(VaultPaths paths, string filePath)
    {
        var relative = Path.GetRelativePath(paths.RootPath, Path.GetFullPath(filePath));
        return relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative);
    }

    private static string CreateUniqueId(ISet<string> existingIds)
    {
        string id;
        do
        {
            id = $"notey-task-{Guid.NewGuid():N}"[..23];
        }
        while (existingIds.Contains(id));

        return id;
    }

    private static string CreateLegacyId(int lineIndex, string line)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{lineIndex}:{line}"))).ToLowerInvariant();
        return $"legacy-{lineIndex}-{hash[..12]}";
    }

    private static string NormalizeTaskText(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task WriteUtf8AtomicallyAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Task file path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        var tempFilePath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempFilePath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    [GeneratedRegex(@"^\s*-\s+\[(?<checked>[ xX])\]\s+(?<body>.+)$")]
    private static partial Regex TaskLineRegex();

    [GeneratedRegex(@"\s+\^(?<id>[A-Za-z0-9_-]+)\s*$")]
    private static partial Regex BlockIdRegex();

    [GeneratedRegex(@"\s+\(due:\s*(?<date>\d{4}-\d{2}-\d{2})\)\s*$")]
    private static partial Regex DueDateRegex();

    [GeneratedRegex(@"\s+\(completed:\s*(?<date>\d{4}-\d{2}-\d{2})\)\s*$")]
    private static partial Regex CompletedDateRegex();

    [GeneratedRegex(@"\s+\(source:\s*\[\[(?<target>[^\]]+)\]\]\)\s*$")]
    private static partial Regex SourceRegex();

    private sealed record TaskMarkdownDocument(List<string> Lines, IReadOnlyList<TaskMarkdownLine> Tasks);

    private sealed record TaskMarkdownLine(NoteyTask Task, int LineIndex);
}
