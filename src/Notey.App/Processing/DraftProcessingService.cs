using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Notey.AI.Providers;
using Notey.App.Imports;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Ocr;
using Notey.Vault.Abstractions;
using Notey.Vault.Documents;
using Notey.Vault.Linking;
using Notey.Vault.Notes;
using Notey.Vault.Tasks;

namespace Notey.App.Processing;

public sealed partial class DraftProcessingService(
    NoteyOptions options,
    IVaultWorkspace workspace,
    IDocumentStoreIndex documentStoreIndex,
    IAiProviderRegistry aiProviderRegistry,
    ITesseractOcrEngine ocrEngine,
    TimeProvider timeProvider,
    ILogger<DraftProcessingService> logger,
    ITaskStore? taskStore = null,
    DraftAttachmentPromoter? attachmentPromoter = null)
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly NoteDirectiveParser _directiveParser = new();
    private readonly ObsidianLinkBuilder _taskLinkBuilder = new(workspace);
    private readonly ITaskStore _taskStore = taskStore ?? new FileSystemTaskStore(workspace, new ObsidianLinkBuilder(workspace), timeProvider);
    private readonly DraftAttachmentPromoter _attachmentPromoter = attachmentPromoter ?? new DraftAttachmentPromoter(workspace);

    public async Task<DraftProcessingResult> ProcessAsync(
        NoteDraft draft,
        string content,
        IReadOnlyList<string>? directOcrSnippets = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(content);

        logger.LogInformation("Processing draft {DraftFile}.", Path.GetFileName(draft.FilePath));

        var paths = workspace.GetPaths();
        var commands = await documentStoreIndex.GetFolderCommandsAsync(cancellationToken);
        var parsed = _directiveParser.Parse(content, commands.Select(static command => command.CommandName));
        var body = parsed.Body.Trim();
        var hasTasks = parsed.Tasks.Count > 0;
        var ocrSnippets = (directOcrSnippets ?? [])
            .Where(static snippet => !string.IsNullOrWhiteSpace(snippet))
            .Select(static snippet => snippet.Trim())
            .ToList();
        if (string.IsNullOrWhiteSpace(body) && !hasTasks && ocrSnippets.Count == 0)
        {
            logger.LogInformation("Draft {DraftFile} skipped — no note content.", Path.GetFileName(draft.FilePath));
            return DraftProcessingResult.Skipped(draft.FilePath, "Draft has no note content.");
        }

        ocrSnippets.AddRange(await OcrIncludedImagesAsync(body, paths, cancellationToken));

        var aiResult = string.IsNullOrWhiteSpace(body) && ocrSnippets.Count == 0
            ? ProcessedNoteAiResult.Empty
            : await RunAiAsync(parsed, commands, body, ocrSnippets, cancellationToken);
        var localNow = timeProvider.GetLocalNow();
        var writtenPaths = new List<string>();
        string? sourceFilePath = null;

        cancellationToken.ThrowIfCancellationRequested();

        ProcessedNoteRoute? route = null;
        string? finalBody = null;
        ProcessedNoteMetadata? metadata = null;
        var shouldWriteNote = !string.IsNullOrWhiteSpace(body) || ocrSnippets.Count > 0;
        if (shouldWriteNote)
        {
            route = ResolveRoute(paths, commands, parsed, aiResult, localNow);
            finalBody = string.IsNullOrWhiteSpace(aiResult.Body)
                ? string.IsNullOrWhiteSpace(body) ? string.Join("\n\n", ocrSnippets) : body
                : aiResult.Body.Trim();
            metadata = BuildMetadata(parsed, aiResult, draft.CreatedAt, localNow);
            if (!route.AppendIfExists || !File.Exists(route.FilePath))
            {
                var filePath = route.AppendIfExists ? route.FilePath : GetUniqueFilePath(route.FilePath);
                route = route with { FilePath = filePath };
            }

            sourceFilePath = route.FilePath;
        }

        IReadOnlyList<NoteyTask> createdTasks = [];
        if (hasTasks)
        {
            logger.LogInformation("Saving {TaskCount} task(s) from draft {DraftFile}.", parsed.Tasks.Count, Path.GetFileName(draft.FilePath));
            createdTasks = await _taskStore.AddAsync(
                parsed.Tasks.Select(task => new NewNoteyTask(task.Text, task.DueDate, sourceFilePath)).ToArray(),
                DateOnly.FromDateTime(localNow.DateTime),
                cancellationToken);
            writtenPaths.Add(_taskStore.GetTasksFilePath());
        }

        if (route is not null && finalBody is not null && metadata is not null)
        {
            DraftAttachmentPromotionResult? attachmentPromotion = null;
            try
            {
                attachmentPromotion = await _attachmentPromoter.PromoteAsync(draft.FilePath, finalBody, route.FilePath, cancellationToken);
                finalBody = attachmentPromotion.Markdown;

                string markdown;
                if (route.AppendIfExists && File.Exists(route.FilePath))
                {
                    markdown = await BuildExistingNoteUpdateAsync(route.FilePath, finalBody, metadata, localNow, cancellationToken);
                }
                else
                {
                    markdown = RenderNewDocument(finalBody, metadata);
                }

                if (createdTasks.Count > 0)
                {
                    markdown = AddTaskBacklinksToMarkdown(markdown, createdTasks);
                }

                await WriteUtf8AtomicallyAsync(route.FilePath, markdown, cancellationToken);
            }
            catch
            {
                if (attachmentPromotion is not null)
                {
                    DraftAttachmentPromoter.DeleteFiles(attachmentPromotion.PromotedPaths);
                }

                await _taskStore.RemoveAsync(createdTasks.Select(static task => task.Id).ToArray(), cancellationToken);
                throw;
            }

            logger.LogInformation("Draft {DraftFile} written to note {DestinationFileName}.", Path.GetFileName(draft.FilePath), Path.GetFileName(route.FilePath));
            writtenPaths.Add(route.FilePath);
            writtenPaths.AddRange(attachmentPromotion.PromotedPaths);
        }

        DeleteIfExists(draft.FilePath);
        _attachmentPromoter.DeleteDraftAssetsDirectory(draft.FilePath, logger);
        return DraftProcessingResult.Completed(draft.FilePath, writtenPaths);
    }

    public async Task SaveExistingNoteAsync(
        string filePath,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(markdown);

        await WriteUtf8AtomicallyAsync(filePath, markdown, cancellationToken);
    }

    public async Task<string> ProcessExistingNoteAsync(
        string filePath,
        string markdown,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(markdown);

        logger.LogInformation("Processing existing note {NoteFile}.", Path.GetFileName(filePath));

        var paths = workspace.GetPaths();
        var commands = await documentStoreIndex.GetFolderCommandsAsync(cancellationToken);
        var normalizedMarkdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var (frontmatter, body) = SplitFrontmatter(normalizedMarkdown);
        var existingTasks = ExtractTaskDirectivesFromExistingBody(body);
        var parsed = ParseExistingNoteDirectives(frontmatter, existingTasks.Body);
        var ocrSnippets = await OcrIncludedImagesAsync(existingTasks.Body, paths, cancellationToken);
        var aiResult = string.IsNullOrWhiteSpace(existingTasks.Body) && ocrSnippets.Count == 0
            ? ProcessedNoteAiResult.Empty
            : await RunAiAsync(parsed, commands, existingTasks.Body, ocrSnippets, cancellationToken);
        var finalBody = string.IsNullOrWhiteSpace(aiResult.Body)
            ? string.IsNullOrWhiteSpace(existingTasks.Body) ? string.Join("\n\n", ocrSnippets) : existingTasks.Body
            : aiResult.Body.Trim();
        var localNow = timeProvider.GetLocalNow();
        var metadata = BuildMetadata(parsed, aiResult, createdAt, localNow);
        var updatedMarkdown = RenderProcessedDocument(normalizedMarkdown, finalBody, metadata);

        IReadOnlyList<NoteyTask> createdTasks = [];
        if (existingTasks.Tasks.Count > 0)
        {
            createdTasks = await _taskStore.AddAsync(
                existingTasks.Tasks.Select(task => new NewNoteyTask(task.Text, task.DueDate, filePath)).ToArray(),
                DateOnly.FromDateTime(localNow.DateTime),
                cancellationToken);
            updatedMarkdown = AddTaskBacklinksToMarkdown(updatedMarkdown, createdTasks);
        }

        try
        {
            await WriteUtf8AtomicallyAsync(filePath, updatedMarkdown, cancellationToken);
        }
        catch
        {
            await _taskStore.RemoveAsync(createdTasks.Select(static task => task.Id).ToArray(), cancellationToken);
            throw;
        }

        return updatedMarkdown;
    }

    private async Task<ProcessedNoteAiResult> RunAiAsync(
        ParsedNoteDirectives parsed,
        IReadOnlyList<VaultFolderCommand> commands,
        string body,
        IReadOnlyList<string> ocrSnippets,
        CancellationToken cancellationToken)
    {
        if (!aiProviderRegistry.TryGet(options.Ai.DefaultProviderId, out var provider))
        {
            logger.LogDebug("AI provider '{ProviderId}' not configured; falling back to unprocessed output.", options.Ai.DefaultProviderId);
            return CreateFallbackAiResult(body, ocrSnippets);
        }

        try
        {
            logger.LogDebug("Invoking AI provider '{ProviderId}' for note processing.", provider.Id);
            var response = await provider.CompleteTextAsync(
                new AiTextRequest(
                    BuildPrompt(parsed, commands, string.IsNullOrWhiteSpace(body) ? "(no typed note text)" : body, ocrSnippets),
                    "You process captured markdown notes for Obsidian. Return only JSON.",
                    options.Ai.ModelName,
                    JsonOutput: true,
                    Temperature: 0.1,
                    MaxTokens: 1600),
                cancellationToken);

            logger.LogDebug("AI provider '{ProviderId}' returned a response.", provider.Id);
            return ProcessedNoteAiResult.Parse(response.Text);
        }
        catch (AiProviderException ex) when (IsAiConfigurationError(ex))
        {
            logger.LogWarning(ex, "AI provider '{ProviderId}' is not fully configured; falling back to unprocessed output.", options.Ai.DefaultProviderId);
            return CreateFallbackAiResult(body, ocrSnippets);
        }
    }

    private static string BuildPrompt(
        ParsedNoteDirectives parsed,
        IReadOnlyList<VaultFolderCommand> commands,
        string body,
        IReadOnlyList<string> ocrSnippets)
    {
        return $$"""
            Process this captured note. The slash directive lines have already been removed from the body.

            Return JSON using this shape:
            {
              "title": "short title used when no /topic is present",
              "filename": "optional safe filename stem",
              "body": "formatted markdown body without slash directive lines",
              "people": ["person name"],
              "tags": ["tag"],
              "links": ["Obsidian link target or URL"]
            }

            Guidelines:
            - "filename" should describe the subject or agenda of the note (e.g. "budget-review", "onboarding-plan").
            - For meeting notes, base "filename" on the meeting topic or agenda, not on attendee names. If no clear topic is available, use "meeting" as the fallback stem.
            - Omit "filename" when a /topic directive is already provided.
            - "tags" must not include a leading '#' character.
            - Preserve Obsidian wiki links and file attachment references from the note body unless the linked text is duplicated verbatim elsewhere.

            Routing metadata:
            - meeting: {{parsed.IsMeeting}}
            - topic: {{parsed.Topic ?? "none"}}
            - dynamic directives: {{string.Join(", ", parsed.DynamicDirectives.Select(static item => $"{item.CommandName}={item.Value}"))}}
            - available dynamic commands: {{string.Join(", ", commands.Select(static command => $"/{command.CommandName} -> {command.FolderName}"))}}

            OCR text:
            {{(ocrSnippets.Count == 0 ? "none" : string.Join("\n\n---\n\n", ocrSnippets))}}

            Note body:
            {{body}}
            """;
    }

    private async Task<IReadOnlyList<string>> OcrIncludedImagesAsync(
        string body,
        VaultPaths paths,
        CancellationToken cancellationToken)
    {
        var snippets = new List<string>();
        foreach (Match match in ObsidianImageEmbedRegex().Matches(body))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imagePath = ResolveVaultImagePath(paths, match.Groups["path"].Value);
            if (imagePath is null || !File.Exists(imagePath))
            {
                continue;
            }

            var result = await ocrEngine.RecognizeAsync(
                new TesseractOcrRequest(
                    imagePath,
                    options.Ocr.DefaultLanguage,
                    string.IsNullOrWhiteSpace(options.Ocr.TesseractDataPath) ? null : options.Ocr.TesseractDataPath),
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                snippets.Add($"Image {match.Groups["path"].Value}: {result.Text.Trim()}");
            }
        }

        return snippets;
    }

    private static string? ResolveVaultImagePath(VaultPaths paths, string embedPath)
    {
        var normalizedEmbed = embedPath.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalizedEmbed))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(paths.RootPath, normalizedEmbed));
        var relative = Path.GetRelativePath(paths.RootPath, candidate);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
        {
            return null;
        }

        return candidate;
    }

    private static ProcessedNoteRoute ResolveRoute(
        VaultPaths paths,
        IReadOnlyList<VaultFolderCommand> commands,
        ParsedNoteDirectives parsed,
        ProcessedNoteAiResult aiResult,
        DateTimeOffset localNow)
    {
        var title = FirstNonEmpty(parsed.Topic, aiResult.Filename, aiResult.Title, "note");
        var fileStem = ToFileStem(title);
        var primaryDynamic = parsed.DynamicDirectives.FirstOrDefault();
        var dynamicCommand = primaryDynamic is null
            ? null
            : commands.FirstOrDefault(command => string.Equals(command.CommandName, primaryDynamic.CommandName, StringComparison.OrdinalIgnoreCase));

        string folderPath;
        if (dynamicCommand is not null && primaryDynamic is not null)
        {
            folderPath = Path.Combine(paths.NotesPath, dynamicCommand.FolderName, ObsidianLinkBuilder.GetSafeFileStem(primaryDynamic.Value));
        }
        else
        {
            folderPath = paths.NotesPath;
        }

        if (parsed.IsMeeting)
        {
            var meetingsPath = Path.Combine(folderPath, "Meetings");
            var meetingStem = $"{localNow:yyyy-MM-dd} - {fileStem}";
            return new ProcessedNoteRoute(Path.Combine(meetingsPath, $"{meetingStem}.md"), AppendIfExists: false);
        }

        return new ProcessedNoteRoute(Path.Combine(folderPath, $"{fileStem}.md"), AppendIfExists: true);
    }

    private static ProcessedNoteMetadata BuildMetadata(
        ParsedNoteDirectives parsed,
        ProcessedNoteAiResult aiResult,
        DateTimeOffset createdAt,
        DateTimeOffset processedAt)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (parsed.IsMeeting)
        {
            tags.Add("meeting");
        }

        foreach (var tag in aiResult.Tags)
        {
            tags.Add(tag.Trim().TrimStart('#'));
        }

        return new ProcessedNoteMetadata(
            createdAt,
            processedAt,
            parsed.IsMeeting,
            parsed.Topic,
            parsed.DynamicDirectives,
            aiResult.People,
            tags.Where(static tag => !string.IsNullOrWhiteSpace(tag)).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            aiResult.Links);
    }

    private static async Task<string> BuildExistingNoteUpdateAsync(
        string filePath,
        string body,
        ProcessedNoteMetadata metadata,
        DateTimeOffset localNow,
        CancellationToken cancellationToken)
    {
        var existing = await File.ReadAllTextAsync(filePath, cancellationToken);
        existing = ApplyMetadata(existing, metadata);
        var heading = $"## {localNow:yyyy-MM-dd}";
        var separator = existing.EndsWith('\n') ? string.Empty : "\n";
        if (!ContainsLine(existing, heading))
        {
            existing = $"{existing}{separator}\n{heading}\n\n{body.Trim()}\n";
        }
        else
        {
            existing = $"{existing}{separator}\n{body.Trim()}\n";
        }

        return existing;
    }

    private static string RenderNewDocument(string body, ProcessedNoteMetadata metadata)
    {
        return $"{RenderFrontmatter(metadata)}\n{body.Trim()}\n";
    }

    private static string RenderProcessedDocument(string markdown, string body, ProcessedNoteMetadata metadata)
    {
        var updatedMarkdown = ApplyMetadata(markdown, metadata);
        var (frontmatter, _) = SplitFrontmatter(updatedMarkdown);
        return $"{frontmatter}\n{body.Trim()}\n";
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

    private static string ApplyMetadata(string markdown, ProcessedNoteMetadata metadata)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return $"{RenderFrontmatter(metadata)}\n{normalized.TrimStart()}";
        }

        var endIndex = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return $"{RenderFrontmatter(metadata)}\n{normalized.TrimStart()}";
        }

        var body = normalized[(endIndex + "\n---".Length)..].TrimStart('\n');
        var frontmatter = normalized[..(endIndex + "\n---".Length)];
        var mergedMetadata = MergeMetadata(frontmatter, metadata);
        return $"{RenderFrontmatter(mergedMetadata)}\n{body}";
    }

    private static ProcessedNoteMetadata MergeMetadata(string frontmatter, ProcessedNoteMetadata metadata)
    {
        var existingDynamicDirectives = ReadYamlDynamicDirectives(frontmatter);
        var existingDateScalar = ReadYamlScalar(frontmatter, "date");
        var existingMeetingDate = existingDateScalar is not null && DateOnly.TryParse(existingDateScalar, out var parsed)
            ? parsed
            : (DateOnly?)null;

        return metadata with
        {
            IsMeeting = metadata.IsMeeting || ReadYamlBoolean(frontmatter, "meeting"),
            Topic = FirstNonEmpty(metadata.Topic, ReadYamlScalar(frontmatter, "topic")),
            DynamicDirectives = UnionDynamicDirectives(existingDynamicDirectives, metadata.DynamicDirectives),
            People = Union(ReadYamlArray(frontmatter, "people"), metadata.People),
            Tags = Union(ReadYamlArray(frontmatter, "tags").Select(static t => t.TrimStart('#')).ToArray(), metadata.Tags),
            Links = Union(ReadYamlArray(frontmatter, "links"), metadata.Links),
            MeetingDate = existingMeetingDate ?? metadata.MeetingDate
        };
    }

    private static string RenderFrontmatter(ProcessedNoteMetadata metadata)
    {
        var lines = new List<string>
        {
            "---",
            $"created: {FormatTimestamp(metadata.CreatedAt)}",
            $"processed: {FormatTimestamp(metadata.ProcessedAt)}",
        };

        if (metadata.IsMeeting)
        {
            lines.Add("meeting: true");
            var meetingDate = metadata.MeetingDate ?? DateOnly.FromDateTime(metadata.CreatedAt.LocalDateTime);
            lines.Add($"date: {meetingDate:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Topic))
        {
            lines.Add($"topic: \"{EscapeYaml(metadata.Topic)}\"");
        }

        foreach (var directive in metadata.DynamicDirectives)
        {
            lines.Add($"{directive.CommandName}: \"{EscapeYaml(directive.Value)}\"");
        }

        AppendYamlArray(lines, "people", metadata.People);
        AppendYamlArray(lines, "tags", metadata.Tags);
        AppendYamlArray(lines, "links", metadata.Links);
        lines.Add("---");
        return string.Join('\n', lines);
    }

    private static ParsedNoteDirectives ParseExistingNoteDirectives(string frontmatter, string body)
    {
        return new ParsedNoteDirectives(
            ReadYamlBoolean(frontmatter, "meeting"),
            ReadYamlScalar(frontmatter, "topic"),
            [],
            ReadYamlDynamicDirectives(frontmatter),
            [],
            body.Trim());
    }

    private (string Body, IReadOnlyList<NoteTaskDirective> Tasks) ExtractTaskDirectivesFromExistingBody(string body)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var bodyLines = new List<string>(lines.Length);
        var tasks = new List<NoteTaskDirective>();
        var isInFencedCode = false;

        foreach (var line in lines)
        {
            var trimmedStart = line.TrimStart();
            if (trimmedStart.StartsWith("```", StringComparison.Ordinal) || trimmedStart.StartsWith("~~~", StringComparison.Ordinal))
            {
                isInFencedCode = !isInFencedCode;
                bodyLines.Add(line);
                continue;
            }

            if (!isInFencedCode && trimmedStart.StartsWith("/task", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = _directiveParser.Parse(trimmedStart, []);
                if (parsed.Tasks.Count == 1 && string.IsNullOrWhiteSpace(parsed.Body))
                {
                    tasks.Add(parsed.Tasks[0]);
                    continue;
                }
            }

            bodyLines.Add(line);
        }

        return (string.Join('\n', bodyLines).Trim(), tasks);
    }

    private string AddTaskBacklinksToMarkdown(string markdown, IReadOnlyList<NoteyTask> tasks)
    {
        if (tasks.Count == 0)
        {
            return markdown;
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var insertIndex = FindOrCreateTasksHeading(lines);
        foreach (var task in tasks)
        {
            if (lines.Any(line => line.Contains($"#^{task.Id}", StringComparison.Ordinal)))
            {
                continue;
            }

            lines.Insert(insertIndex, $"- {BuildTaskBacklink(task)}");
            insertIndex++;
        }

        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    private string BuildTaskBacklink(NoteyTask task)
    {
        var tasksLinkPath = _taskLinkBuilder.GetLinkPath(workspace.GetPaths(), _taskStore.GetTasksFilePath());
        return ObsidianLinkBuilder.FormatWikiLink($"{tasksLinkPath}#^{task.Id}", $"Task: {task.Text}");
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

    private static IReadOnlyList<string> ReadYamlArray(string frontmatter, string key)
    {
        var lines = frontmatter.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!line.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(key.Length + 1)..].Trim();
            if (value == "[]")
            {
                return [];
            }

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                return value.Trim('[', ']')
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Unquote)
                    .ToArray();
            }

            var values = new List<string>();
            for (var child = index + 1; child < lines.Length; child++)
            {
                if (!TryReadYamlArrayItem(lines[child], out var item))
                {
                    break;
                }

                values.Add(Unquote(item));
            }

            return values;
        }

        return [];
    }

    private static void AppendYamlArray(ICollection<string> lines, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            lines.Add($"{key}: []");
            return;
        }

        lines.Add($"{key}:");
        foreach (var value in values)
        {
            lines.Add($"  - \"{EscapeYaml(value)}\"");
        }
    }

    private static IReadOnlyList<string> Union(IEnumerable<string> existing, IEnumerable<string> additions)
    {
        return existing.Concat(additions)
            .Select(static item => item.Trim())
            .Where(static item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DynamicNoteDirective> UnionDynamicDirectives(
        IEnumerable<DynamicNoteDirective> existing,
        IEnumerable<DynamicNoteDirective> additions)
    {
        return existing
            .Concat(additions)
            .Where(static directive => !string.IsNullOrWhiteSpace(directive.CommandName) && !string.IsNullOrWhiteSpace(directive.Value))
            .GroupBy(
                static directive => $"{directive.CommandName.Trim()}:{directive.Value.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-ddTHH:mmzzz", CultureInfo.InvariantCulture);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "note";
    }

    private static ProcessedNoteAiResult CreateFallbackAiResult(string body, IReadOnlyList<string> ocrSnippets)
    {
        var fallbackTitle = BuildFallbackTitle(body, ocrSnippets);
        return new ProcessedNoteAiResult(fallbackTitle, fallbackTitle, null, [], [], []);
    }

    private static string BuildFallbackTitle(string body, IReadOnlyList<string> ocrSnippets)
    {
        foreach (var candidate in EnumerateFallbackTitleCandidates(body, ocrSnippets))
        {
            var normalized = NormalizeFallbackTitleCandidate(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return "note";
    }

    private static IEnumerable<string> EnumerateFallbackTitleCandidates(string body, IReadOnlyList<string> ocrSnippets)
    {
        foreach (var line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }

        foreach (var snippet in ocrSnippets)
        {
            foreach (var line in snippet.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    yield return line;
                }
            }
        }
    }

    private static string NormalizeFallbackTitleCandidate(string candidate)
    {
        var trimmed = candidate.Trim();
        while (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        trimmed = trimmed.Trim('*', '-', '_', '`', '>', '[', ']', '(', ')', ':', ';', '.', ',', '!', '?', '"', '\'');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed.Length <= 80 ? trimmed : trimmed[..80].Trim();
    }

    private static bool IsAiConfigurationError(AiProviderException exception)
    {
        return exception.Message.Contains("has no configured base URL", StringComparison.Ordinal)
            || exception.Message.Contains("has no API key", StringComparison.Ordinal)
            || exception.Message.Contains("has no configured model name", StringComparison.Ordinal);
    }

    private static string ToFileStem(string title)
    {
        return ObsidianLinkBuilder.GetSafeFileStem(title).ToLowerInvariant();
    }

    private static string GetUniqueFilePath(string filePath)
    {
        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = index == 1 ? filePath : WithSuffix(filePath, index);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique processed note filename.");
    }

    private static string WithSuffix(string filePath, int suffix)
    {
        var directory = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        return string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}-{suffix}{extension}"
            : Path.Combine(directory, $"{fileName}-{suffix}{extension}");
    }

    private static async Task WriteUtf8AtomicallyAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Target file path must include a directory.");
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
            DeleteIfExists(tempFilePath);
        }
    }

    private static bool ContainsLine(string text, string line)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Any(candidate => string.Equals(candidate.TrimEnd(), line, StringComparison.Ordinal));
    }

    private static void EnsureEndsWithNewline(StringBuilder builder)
    {
        if (builder.Length == 0 || builder[^1] != '\n')
        {
            builder.Append('\n');
        }
    }

    private static bool TryReadYamlArrayItem(string line, out string value)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
        {
            value = string.Empty;
            return false;
        }

        value = trimmed[2..].Trim();
        return true;
    }

    private static bool ReadYamlBoolean(string frontmatter, string key)
    {
        var value = ReadYamlScalar(frontmatter, key);
        return bool.TryParse(value, out var parsed) && parsed;
    }

    private static string? ReadYamlScalar(string frontmatter, string key)
    {
        foreach (var line in frontmatter.Split('\n'))
        {
            if (!line.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Unquote(line[(key.Length + 1)..].Trim());
        }

        return null;
    }

    private static IReadOnlyList<DynamicNoteDirective> ReadYamlDynamicDirectives(string frontmatter)
    {
        var reservedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "created",
            "processed",
            "meeting",
            "date",
            "topic",
            "people",
            "tags",
            "links"
        };

        var directives = new List<DynamicNoteDirective>();
        foreach (var line in frontmatter.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (key.Length == 0 || reservedKeys.Contains(key) || key.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var value = Unquote(line[(separator + 1)..].Trim());
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            directives.Add(new DynamicNoteDirective(key, value, 0));
        }

        return directives;
    }

    private static string EscapeYaml(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) => $"\\u{(int)character:x4}",
                _ => character.ToString()
            });
        }

        return builder.ToString();
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? UnescapeYamlQuoted(trimmed[1..^1])
            : trimmed;
    }

    private static string UnescapeYamlQuoted(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index == value.Length - 1)
            {
                builder.Append(character);
                continue;
            }

            var escaped = value[++index];
            switch (escaped)
            {
                case '\\':
                    builder.Append('\\');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u' when index + 4 < value.Length
                    && int.TryParse(value.AsSpan(index + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var unicode):
                    builder.Append((char)unicode);
                    index += 4;
                    break;
                default:
                    builder.Append(escaped);
                    break;
            }
        }

        return builder.ToString();
    }

    private static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    [GeneratedRegex(@"!\[\[(?<path>[^\]|]+)(?:\|[^\]]+)?\]\]")]
    private static partial Regex ObsidianImageEmbedRegex();
}

public sealed record DraftProcessingResult(
    bool Processed,
    string DraftPath,
    IReadOnlyList<string> WrittenPaths,
    string? Message)
{
    public static DraftProcessingResult Skipped(string draftPath, string message)
    {
        return new DraftProcessingResult(false, draftPath, [], message);
    }

    public static DraftProcessingResult Completed(string draftPath, IReadOnlyList<string> writtenPaths)
    {
        return new DraftProcessingResult(true, draftPath, writtenPaths, null);
    }
}

public sealed record ProcessedNoteRoute(string FilePath, bool AppendIfExists);

public sealed record ProcessedNoteMetadata(
    DateTimeOffset CreatedAt,
    DateTimeOffset ProcessedAt,
    bool IsMeeting,
    string? Topic,
    IReadOnlyList<DynamicNoteDirective> DynamicDirectives,
    IReadOnlyList<string> People,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Links,
    DateOnly? MeetingDate = null);

public sealed record ProcessedNoteAiResult(
    string? Title,
    string? Filename,
    string? Body,
    IReadOnlyList<string> People,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Links)
{
    public static ProcessedNoteAiResult Empty { get; } = new(null, null, null, [], [], []);

    public static ProcessedNoteAiResult Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Processed note AI response must be a JSON object.");
        }

        return new ProcessedNoteAiResult(
            ReadString(root, "title"),
            ReadString(root, "filename"),
            ReadString(root, "body"),
            ReadStringArray(root, "people"),
            ReadStringArray(root, "tags"),
            ReadStringArray(root, "links"));
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }
}
