using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.AI.Providers;
using Notey.App.Imports;
using Notey.App.Processing;
using Notey.Core.Configuration;
using Notey.Ocr;
using Notey.Vault.Abstractions;
using Notey.Vault.Documents;
using Notey.Vault.Notes;

namespace Notey.Tests;

public sealed class DraftProcessingServiceTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task ProcessAsync_creates_meeting_under_first_dynamic_folder()
    {
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Customers"));
        var service = CreateService(rootPath, """{ "body": "Keep the accounts safe.", "people": [{ "name": "Jane Doe", "confidence": 0.91 }], "tags": [{ "name": "accounts", "confidence": 0.82 }] }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/meeting\n/customer Microsoft\n/topic Accounts\n\nKeep accounts safe.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        var result = await service.ProcessAsync(draft, draft.Content);

        Assert.True(result.Processed);
        var target = Path.Combine(rootPath, "Notes", "Customers", "Microsoft", "Meetings", "2026-05-13 - accounts.md");
        Assert.True(File.Exists(target));
        var content = await File.ReadAllTextAsync(target);
        Assert.Contains("meeting: true", content);
        Assert.Contains("date: 2026-05-13", content);
        Assert.Contains("customer: \"Microsoft\"", content);
        Assert.Contains("topic: \"Accounts\"", content);
        Assert.Contains("Keep the accounts safe.", content);
        Assert.False(File.Exists(draft.FilePath));
    }

    [Fact]
    public async Task ProcessAsync_does_not_add_date_field_for_non_meeting_note()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, """{ "body": "Non-meeting content." }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var target = Path.Combine(rootPath, "Notes", "accounts.md");
        var content = await File.ReadAllTextAsync(target);
        Assert.DoesNotContain("date:", content);
        Assert.DoesNotContain("meeting:", content);
    }

    [Fact]
    public async Task ProcessAsync_filters_ai_people_and_tags_by_confidence_and_tag_length()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, """
            {
              "body": "Discussed launch focus.",
              "people": [
                { "name": "Jane Doe", "confidence": 0.91 },
                { "name": "Sam Low", "confidence": 0.42 }
              ],
              "tags": [
                { "name": "focus", "confidence": 0.8 },
                { "name": "too many words", "confidence": 0.99 },
                { "name": "weak", "confidence": 0.2 }
              ]
            }
            """);
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var target = Path.Combine(rootPath, "Notes", "accounts.md");
        var content = await File.ReadAllTextAsync(target);
        Assert.Contains("  - \"[[People/Jane Doe|Jane Doe]]\"", content);
        Assert.DoesNotContain("Sam Low", content);
        Assert.Contains("  - \"focus\"", content);
        Assert.DoesNotContain("too many words", content);
        Assert.DoesNotContain("weak", content);
        Assert.True(File.Exists(Path.Combine(rootPath, "People", "Jane Doe.md")));
    }

    [Fact]
    public async Task ProcessAsync_ignores_unscored_legacy_people_and_tags()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, """{ "body": "Legacy response.", "people": ["Jane Doe"], "tags": ["legacy"] }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var content = await File.ReadAllTextAsync(Path.Combine(rootPath, "Notes", "accounts.md"));
        Assert.DoesNotContain("People/Jane Doe", content);
        Assert.DoesNotContain("legacy", content);
        Assert.False(File.Exists(Path.Combine(rootPath, "People", "Jane Doe.md")));
    }

    [Fact]
    public async Task ProcessAsync_preserves_existing_person_wiki_links_when_linking_ai_people()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, """
            {
              "body": "Met with [[People/Jane Doe|JD]] about launch.",
              "people": [{ "name": "Jane Doe", "confidence": 0.91 }]
            }
            """);
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var content = await File.ReadAllTextAsync(Path.Combine(rootPath, "Notes", "accounts.md"));
        Assert.Contains("Met with [[People/Jane Doe|JD]] about launch.", content);
        Assert.DoesNotContain("[[People/[[People/Jane Doe|Jane Doe]]|JD]]", content);
        Assert.DoesNotContain("People: [[People/Jane Doe|Jane Doe]]", content);
    }

    [Fact]
    public async Task ProcessExistingNoteAsync_preserves_meeting_date_field_when_reprocessing()
    {
        var rootPath = CreateTempDirectory();
        var target = Path.Combine(rootPath, "Notes", "Customers", "Microsoft", "Meetings", "2026-05-01 - accounts.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, """
            ---
            created: 2026-05-01T00:00:00.0000000+00:00
            processed: 2026-05-01T00:00:00.0000000+00:00
            meeting: true
            date: 2026-05-01
            topic: "Accounts"
            people: []
            tags: []
            links: []
            ---
            Original body.
            """);
        var service = CreateService(rootPath, """{ "body": "Updated body." }""");

        var updated = await service.ProcessExistingNoteAsync(
            target,
            """
            ---
            created: 2026-05-01T00:00:00.0000000+00:00
            processed: 2026-05-01T00:00:00.0000000+00:00
            meeting: true
            date: 2026-05-01
            topic: "Accounts"
            people: []
            tags: []
            links: []
            ---
            Original body.
            """,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("date: 2026-05-01", updated);
    }

    [Fact]
    public async Task ProcessExistingNoteAsync_uses_created_date_as_meeting_date_fallback_for_legacy_notes()
    {
        var rootPath = CreateTempDirectory();
        var target = Path.Combine(rootPath, "Notes", "Customers", "Microsoft", "Meetings", "2026-03-10 - accounts.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, """
            ---
            created: 2026-03-10T09:00:00.0000000+00:00
            processed: 2026-03-10T09:01:00.0000000+00:00
            meeting: true
            topic: "Accounts"
            people: []
            tags: []
            links: []
            ---
            Original body.
            """);
        var service = CreateService(rootPath, """{ "body": "Updated body." }""");

        var updated = await service.ProcessExistingNoteAsync(
            target,
            """
            ---
            created: 2026-03-10T09:00:00.0000000+00:00
            processed: 2026-03-10T09:01:00.0000000+00:00
            meeting: true
            topic: "Accounts"
            people: []
            tags: []
            links: []
            ---
            Original body.
            """,
            new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        Assert.Contains("date: 2026-03-10", updated);
        Assert.DoesNotContain($"date: {DateOnly.FromDateTime(DateTimeOffset.UtcNow.LocalDateTime):yyyy-MM-dd}", updated);
    }

    [Fact]
    public async Task ProcessAsync_appends_topic_only_note_under_existing_date_heading()
    {
        var rootPath = CreateTempDirectory();
        var target = Path.Combine(rootPath, "Notes", "accounts.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, """
            ---
            created: 2026-05-01T00:00:00.0000000+00:00
            tags:
              - "#old"
            ---
            Existing intro.

            ## 2026-05-13

            Earlier note.
            """);
        var service = CreateService(rootPath, """{ "body": "New note.", "tags": [{ "name": "new", "confidence": 0.82 }] }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var content = await File.ReadAllTextAsync(target);
        Assert.Single(FindAll(content, "## 2026-05-13"));
        Assert.Contains("Earlier note.", content);
        Assert.Contains("New note.", content);
        Assert.Contains("  - \"old\"", content);
        Assert.Contains("  - \"new\"", content);
    }

    [Fact]
    public async Task ProcessAsync_appends_topic_note_adds_exact_date_heading_when_only_prefix_exists()
    {
        var rootPath = CreateTempDirectory();
        var target = Path.Combine(rootPath, "Notes", "accounts.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, """
            ---
            created: 2026-05-01T00:00:00.0000000+00:00
            ---
            Existing intro.

            ## 2026-05-130

            Earlier note.
            """);
        var service = CreateService(rootPath, """{ "body": "New note." }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var content = await File.ReadAllTextAsync(target);
        Assert.Contains("## 2026-05-130", content);
        Assert.Contains("## 2026-05-13", content);
    }

    [Fact]
    public async Task ProcessAsync_appends_tasks_to_tasks_file()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, """{ "body": "" }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/task Send recap // 2026-05-20", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var tasks = await File.ReadAllTextAsync(Path.Combine(rootPath, "Notes", "tasks.md"));
        Assert.Contains("- [ ] Send recap (due: 2026-05-20)", tasks);
    }

    [Fact]
    public async Task ProcessAsync_links_tasks_to_source_note_and_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, """{ "body": "Launch body." }""");
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            /topic Roadmap
            /task Review launch // 2026-05-20

            Raw launch note.
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content, cancellationToken: cancellationToken);

        var taskContent = await File.ReadAllTextAsync(Path.Combine(rootPath, "Notes", "tasks.md"), cancellationToken);
        Assert.Contains("- [ ] Review launch (due: 2026-05-20)", taskContent);
        Assert.Contains("(source: [[Notes/roadmap|roadmap]])", taskContent);
        var taskId = ExtractTaskId(taskContent);
        var sourceContent = await File.ReadAllTextAsync(Path.Combine(rootPath, "Notes", "roadmap.md"), cancellationToken);
        Assert.Contains($"[[Notes/tasks#^{taskId}|Task: Review launch]]", sourceContent);
    }

    [Fact]
    public async Task ProcessExistingNoteAsync_preserves_literal_task_lines_in_code_fences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var target = Path.Combine(rootPath, "Notes", "roadmap.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var service = CreateServiceWithoutAi(rootPath);
        var content = """
            ---
            created: 2026-05-01T00:00:00.0000000+00:00
            ---
            ```text
            /task literal example // 2026-05-20
            ```
            """;

        var updated = await service.ProcessExistingNoteAsync(
            target,
            content,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            cancellationToken);

        Assert.Contains("/task literal example // 2026-05-20", updated);
        Assert.False(File.Exists(Path.Combine(rootPath, "Notes", "tasks.md")));
    }

    [Fact]
    public async Task ProcessExistingNoteAsync_extracts_indented_task_directives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var target = Path.Combine(rootPath, "Notes", "roadmap.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var service = CreateServiceWithoutAi(rootPath);
        var content = """
            ---
            created: 2026-05-01T00:00:00.0000000+00:00
            ---
              /task Review launch // 2026-05-20
            """;

        var updated = await service.ProcessExistingNoteAsync(
            target,
            content,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            cancellationToken);

        Assert.DoesNotContain("/task Review launch // 2026-05-20", updated);
        var tasksPath = Path.Combine(rootPath, "Notes", "tasks.md");
        var taskContent = await File.ReadAllTextAsync(tasksPath, cancellationToken);
        Assert.Contains("- [ ] Review launch (due: 2026-05-20)", taskContent);
        var taskId = ExtractTaskId(taskContent);
        Assert.Contains($"[[Notes/tasks#^{taskId}|Task: Review launch]]", updated);
    }

    [Fact]
    public async Task ProcessAsync_appends_tasks_under_exact_date_heading()
    {
        var rootPath = CreateTempDirectory();
        var tasksPath = Path.Combine(rootPath, "Notes", "tasks.md");
        Directory.CreateDirectory(Path.GetDirectoryName(tasksPath)!);
        await File.WriteAllTextAsync(tasksPath, """
            # Tasks

            ## 2026-05-130

            - [ ] Existing task
            """);
        var service = CreateService(rootPath, """{ "body": "" }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/task New task", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var tasks = await File.ReadAllTextAsync(tasksPath);
        Assert.Contains("## 2026-05-130", tasks);
        Assert.Contains("## 2026-05-13", tasks);
    }

    [Fact]
    public async Task ProcessAsync_appends_tasks_on_new_line_when_existing_heading_has_no_trailing_newline()
    {
        var rootPath = CreateTempDirectory();
        var tasksPath = Path.Combine(rootPath, "Notes", "tasks.md");
        Directory.CreateDirectory(Path.GetDirectoryName(tasksPath)!);
        await File.WriteAllTextAsync(tasksPath, "# Tasks\n\n## 2026-05-13\n- [ ] Existing task");
        var service = CreateService(rootPath, """{ "body": "" }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/task New task", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var tasks = await File.ReadAllTextAsync(tasksPath);
        Assert.Contains("- [ ] Existing task\n- [ ] New task", tasks);
    }

    [Fact]
    public async Task ProcessAsync_routes_by_first_dynamic_directive()
    {
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Customers"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Partners"));
        var service = CreateService(rootPath, """{ "body": "Partner note." }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/partner Contoso\n/customer Microsoft\n/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "Partners", "Contoso", "accounts.md")));
        Assert.False(File.Exists(Path.Combine(rootPath, "Notes", "Customers", "Microsoft", "accounts.md")));
    }

    [Fact]
    public async Task ProcessAsync_routes_meeting_without_dynamic_folder_under_notes_meetings()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, """{ "body": "Meeting note." }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/meeting\n/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "Meetings", "2026-05-13 - accounts.md")));
    }

    [Fact]
    public async Task ProcessAsync_uses_direct_ocr_snippet_without_typed_body()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, """{ "title": "OCR note", "filename": "ocr-note", "body": "Clean OCR note." }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), string.Empty, new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content, ["screen text"]);

        var content = await File.ReadAllTextAsync(Path.Combine(rootPath, "Notes", "ocr-note.md"));
        Assert.Contains("Clean OCR note.", content);
        Assert.False(File.Exists(draft.FilePath));
    }

    [Fact]
    public async Task ProcessAsync_includes_known_people_context_for_direct_ocr_snippets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        await WriteFileAsync(Path.Combine(rootPath, "People", "James Simpson.md"), """
            ---
            title: James Simpson
            aliases: [Jim Simpson, Simpson James]
            ---
            """);
        var aiProvider = new RecordingAiProvider("""{ "title": "OCR note", "filename": "ocr-note", "body": "Clean OCR note." }""");
        var service = CreateService(rootPath, aiProvider);
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), string.Empty, new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content, ["Jarnes Sirnpson"], cancellationToken: cancellationToken);

        var request = Assert.Single(aiProvider.Requests);
        Assert.Contains("Known People for matching:", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("- James Simpson (aliases: Jim Simpson, Simpson James)", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("Before returning a person name extracted from OCR, first compare it to the known People list.", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("Return the canonical known People name when a match is likely.", request.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_falls_back_to_body_based_filename_when_ai_is_not_configured()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateServiceWithoutAi(rootPath);
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            # Launch checklist

            Confirm the final rollout steps.
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        var result = await service.ProcessAsync(draft, draft.Content);

        Assert.True(result.Processed);
        var targetPath = Path.Combine(rootPath, "Notes", "launch checklist.md");
        Assert.True(File.Exists(targetPath));
        var content = await File.ReadAllTextAsync(targetPath);
        Assert.Contains("Confirm the final rollout steps.", content);
    }

    [Fact]
    public async Task ProcessAsync_falls_back_to_ocr_based_filename_when_ai_is_not_configured()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateServiceWithoutAi(rootPath);
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), string.Empty, new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        var result = await service.ProcessAsync(draft, draft.Content, ["Microsoft Contract Review"]);

        Assert.True(result.Processed);
        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "microsoft contract review.md")));
    }

    [Fact]
    public async Task ProcessAsync_falls_back_when_provider_reports_missing_configuration()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath, new MissingConfigurationAiProvider());
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "Contract notes", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        var result = await service.ProcessAsync(draft, draft.Content);

        Assert.True(result.Processed);
        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "contract notes.md")));
    }

    [Fact]
    public async Task ProcessExistingNoteAsync_preserves_created_timestamp_and_updates_existing_file_in_place()
    {
        var rootPath = CreateTempDirectory();
        var target = Path.Combine(rootPath, "Notes", "accounts.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, """
            ---
            created: 2026-05-01T00:00:00.0000000+00:00
            processed: 2026-05-02T00:00:00.0000000+00:00
            meeting: false
            topic: "Accounts"
            tags:
              - "#old"
            links: []
            ---
            Current body.
            """);
        var service = CreateService(rootPath, """{ "body": "Updated body.", "tags": [{ "name": "new", "confidence": 0.82 }], "links": ["https://example.com"] }""");

        var updated = await service.ProcessExistingNoteAsync(
            target,
            """
            ---
            processed: 2026-05-02T00:00:00.0000000+00:00
            meeting: false
            topic: "Accounts"
            tags:
              - "#old"
            links: []
            ---
            Current body.
            """,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(updated, await File.ReadAllTextAsync(target));
        Assert.Contains("created: 2026-05-01T00:00+00:00", updated);
        Assert.Matches(@"processed: 2026-05-13T\d{2}:\d{2}[+-]\d{2}:\d{2}", updated);
        Assert.Contains("  - \"old\"", updated);
        Assert.Contains("  - \"new\"", updated);
        Assert.Contains("  - \"https://example.com\"", updated);
        Assert.Contains("Updated body.", updated);
    }

    [Fact]
    public async Task ProcessAsync_promotes_staged_draft_attachments_and_rewrites_links()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var service = CreateServiceWithoutAi(rootPath);
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            /topic Roadmap

            See [[Notes/Draft/draft.assets/spec.pdf|spec.pdf]].
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        var draftAssets = AttachmentImportPaths.GetDraftAssetsDirectory(draft.FilePath);
        var stagedAttachment = Path.Combine(draftAssets, "spec.pdf");
        await WriteFileAsync(draft.FilePath, draft.Content);
        await WriteFileAsync(stagedAttachment, "draft attachment");

        var result = await service.ProcessAsync(draft, draft.Content, cancellationToken: cancellationToken);

        var finalAttachment = Path.Combine(rootPath, "Notes", "roadmap.assets", "spec.pdf");
        Assert.True(File.Exists(finalAttachment));
        Assert.False(Directory.Exists(draftAssets));
        Assert.Contains(finalAttachment, result.WrittenPaths);
        var finalContent = await File.ReadAllTextAsync(Path.Combine(rootPath, "Notes", "roadmap.md"), cancellationToken);
        Assert.Contains("[[Notes/roadmap.assets/spec.pdf|spec.pdf]]", finalContent);
        Assert.DoesNotContain("Notes/Draft/draft.assets", finalContent);
    }

    [Fact]
    public async Task ProcessAsync_promotes_staged_attachments_when_appending_to_existing_topic_note()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var target = Path.Combine(rootPath, "Notes", "roadmap.md");
        await WriteFileAsync(target, """
            ---
            created: 2026-05-01T00:00:00.0000000+00:00
            ---
            Existing roadmap.
            """);
        await WriteFileAsync(Path.Combine(rootPath, "Notes", "roadmap.assets", "spec.pdf"), "existing attachment");
        var service = CreateServiceWithoutAi(rootPath);
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            /topic Roadmap

            Updated spec: [[Notes/Draft/draft.assets/spec.pdf|spec.pdf]]
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        var draftAssets = AttachmentImportPaths.GetDraftAssetsDirectory(draft.FilePath);
        await WriteFileAsync(draft.FilePath, draft.Content);
        await WriteFileAsync(Path.Combine(draftAssets, "spec.pdf"), "new attachment");

        await service.ProcessAsync(draft, draft.Content, cancellationToken: cancellationToken);

        var finalAttachment = Path.Combine(rootPath, "Notes", "roadmap.assets", "spec-2.pdf");
        Assert.True(File.Exists(finalAttachment));
        var finalContent = await File.ReadAllTextAsync(target, cancellationToken);
        Assert.Contains("Existing roadmap.", finalContent);
        Assert.Contains("[[Notes/roadmap.assets/spec-2.pdf|spec.pdf]]", finalContent);
    }

    [Fact]
    public async Task ProcessAsync_rejects_draft_attachment_links_that_traverse_outside_staging_folder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var service = CreateServiceWithoutAi(rootPath);
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            /topic Roadmap

            Do not import [[Notes/Draft/draft.assets/../secret.txt|secret.txt]].
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        var draftAssets = AttachmentImportPaths.GetDraftAssetsDirectory(draft.FilePath);
        await WriteFileAsync(draft.FilePath, draft.Content);
        await WriteFileAsync(Path.Combine(draftAssets, "spec.pdf"), "draft attachment");
        await WriteFileAsync(Path.Combine(rootPath, "Notes", "Draft", "secret.txt"), "should never be promoted");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync(draft, draft.Content, cancellationToken: cancellationToken));

        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "Draft", "secret.txt")));
        Assert.False(Directory.Exists(Path.Combine(rootPath, "Notes", "roadmap.assets")));
        Assert.True(File.Exists(draft.FilePath));
    }

    [Fact]
    public async Task ProcessAsync_rolls_back_promoted_attachments_when_final_note_write_fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "blocked.md"));
        var service = CreateServiceWithoutAi(rootPath);
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            /topic Blocked

            Attachment: [[Notes/Draft/draft.assets/spec.pdf|spec.pdf]]
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        var draftAssets = AttachmentImportPaths.GetDraftAssetsDirectory(draft.FilePath);
        var stagedAttachment = Path.Combine(draftAssets, "spec.pdf");
        var finalAttachment = Path.Combine(rootPath, "Notes", "blocked.assets", "spec.pdf");
        await WriteFileAsync(draft.FilePath, draft.Content);
        await WriteFileAsync(stagedAttachment, "draft attachment");

        var ex = await Record.ExceptionAsync(() => service.ProcessAsync(draft, draft.Content, cancellationToken: cancellationToken));
        Assert.NotNull(ex);
        Assert.True(ex is IOException or UnauthorizedAccessException, $"Expected IOException or UnauthorizedAccessException but got {ex.GetType().Name}.");
        Assert.False(File.Exists(finalAttachment));
        Assert.True(File.Exists(stagedAttachment));
        Assert.True(File.Exists(draft.FilePath));
    }

    [Fact]
    public async Task ProcessAsync_skips_included_image_ocr_when_ocr_is_unavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var imagePath = Path.Combine(rootPath, "Images", "photo.png");
        await WriteFileAsync(imagePath, "not real image data");
        var service = CreateService(
            rootPath,
            new RecordingAiProvider("""{ "body": "Processed without OCR." }"""),
            new ThrowingOcrEngine(new OcrDependencyUnavailableException("Native OCR library missing.")));
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            /topic OCR

            Image note: ![[Images/photo.png]]
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        var result = await service.ProcessAsync(draft, draft.Content, cancellationToken: cancellationToken);

        Assert.True(result.Processed);
        Assert.False(File.Exists(draft.FilePath));
        var target = Path.Combine(rootPath, "Notes", "ocr.md");
        var content = await File.ReadAllTextAsync(target, cancellationToken);
        Assert.Contains("Processed without OCR.", content);
    }

    [Fact]
    public async Task ProcessAsync_includes_known_people_context_for_included_image_ocr()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var imagePath = Path.Combine(rootPath, "Images", "photo.png");
        await WriteFileAsync(imagePath, "not real image data");
        await WriteFileAsync(Path.Combine(rootPath, "People", "Michael Browne.md"), """
            ---
            title: Michael Browne
            aliases:
              - Mike Browne
            ---
            """);
        var aiProvider = new RecordingAiProvider("""{ "body": "Processed image OCR." }""");
        var service = CreateService(rootPath, aiProvider, new RecordingOcrEngine("Micheal Brown joined"));
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            /topic OCR

            Image note: ![[Images/photo.png]]
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content, cancellationToken: cancellationToken);

        var request = Assert.Single(aiProvider.Requests);
        Assert.Contains("Image Images/photo.png: Micheal Brown joined", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("Known People for matching:", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("- Michael Browne (aliases: Mike Browne)", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("reversed first/last name order", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("slight spelling differences", request.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_surfaces_non_dependency_ocr_configuration_errors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var imagePath = Path.Combine(rootPath, "Images", "photo.png");
        await WriteFileAsync(imagePath, "not real image data");
        var service = CreateService(
            rootPath,
            new RecordingAiProvider("""{ "body": "Should not be written." }"""),
            new ThrowingOcrEngine(new InvalidOperationException("Bundled tessdata for language 'fra' not found.")));
        var draft = new NoteDraft(
            Path.Combine(rootPath, "Notes", "Draft", "draft.md"),
            """
            /topic OCR

            Image note: ![[Images/photo.png]]
            """,
            new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync(draft, draft.Content, cancellationToken: cancellationToken));

        Assert.True(File.Exists(draft.FilePath));
        Assert.False(File.Exists(Path.Combine(rootPath, "Notes", "ocr.md")));
    }

    [Fact]
    public async Task ProcessOcrSnippetAsync_omits_known_people_context_when_no_people_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        var aiProvider = new RecordingAiProvider("""{ "body": "Unknown attendee." }""");
        var service = CreateService(rootPath, aiProvider);

        await service.ProcessOcrSnippetAsync("Unknown attendee", "Existing note context", cancellationToken);

        var request = Assert.Single(aiProvider.Requests);
        Assert.DoesNotContain("Known People for matching:", request.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessOcrSnippetAsync_includes_known_people_context()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootPath = CreateTempDirectory();
        await WriteFileAsync(Path.Combine(rootPath, "People", "James Simpson.md"), """
            ---
            title: James Simpson
            aliases:
              - Jim Simpson
            ---
            """);
        var aiProvider = new RecordingAiProvider("""{ "body": "Met James Simpson." }""");
        var service = CreateService(rootPath, aiProvider);

        await service.ProcessOcrSnippetAsync("Jarnes Sirnpson", "Existing note context", cancellationToken);

        var request = Assert.Single(aiProvider.Requests);
        Assert.Contains("Known People for matching:", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("- James Simpson (aliases: Jim Simpson)", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("Return the canonical known People name when a match is likely.", request.Prompt, StringComparison.Ordinal);
    }

    private DraftProcessingService CreateService(string rootPath, string aiResponse)
    {
        return CreateService(rootPath, new RecordingAiProvider(aiResponse));
    }

    private DraftProcessingService CreateService(
        string rootPath,
        IAiProvider aiProvider,
        ITesseractOcrEngine? ocrEngine = null)
    {
        var options = new NoteyOptions
        {
            Vault = new VaultOptions { RootPath = rootPath },
            Ai = new AiOptions { DefaultProviderId = "default", ModelName = "test" }
        };
        var workspace = new FileSystemVaultWorkspace(options);
        return new DraftProcessingService(
            options,
            workspace,
            new FileSystemDocumentStoreIndex(workspace),
            new AiProviderRegistry([aiProvider], "default"),
            ocrEngine ?? new RecordingOcrEngine(),
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<DraftProcessingService>.Instance);
    }

    private DraftProcessingService CreateServiceWithoutAi(string rootPath)
    {
        var options = new NoteyOptions
        {
            Vault = new VaultOptions { RootPath = rootPath },
            Ai = new AiOptions { DefaultProviderId = "default", ModelName = string.Empty }
        };
        var workspace = new FileSystemVaultWorkspace(options);
        return new DraftProcessingService(
            options,
            workspace,
            new FileSystemDocumentStoreIndex(workspace),
            new AiProviderRegistry([], "default"),
            new RecordingOcrEngine(),
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<DraftProcessingService>.Instance);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "notey-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static async Task WriteFileAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content);
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static IEnumerable<int> FindAll(string text, string value)
    {
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            yield return index;
            index += value.Length;
        }
    }

    private static string ExtractTaskId(string taskContent)
    {
        var match = Regex.Match(taskContent, @"\^notey-task-[^\s]+", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Expected a persisted task block ID.");
        return match.Value[1..];
    }

    private sealed class RecordingAiProvider(string response) : IAiProvider
    {
        public List<AiTextRequest> Requests { get; } = [];

        public string Id => "default";

        public ValueTask<AiTextResponse> CompleteTextAsync(AiTextRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new AiTextResponse(response, Id, "test"));
        }
    }

    private sealed class RecordingOcrEngine(string text = "ocr text") : ITesseractOcrEngine
    {
        public ValueTask<OcrResult> RecognizeAsync(TesseractOcrRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new OcrResult(text, "eng", 1.0, []));
        }
    }

    private sealed class ThrowingOcrEngine(Exception exception) : ITesseractOcrEngine
    {
        public ValueTask<OcrResult> RecognizeAsync(TesseractOcrRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<OcrResult>(exception);
        }
    }

    private sealed class MissingConfigurationAiProvider : IAiProvider
    {
        public string Id => "default";

        public ValueTask<AiTextResponse> CompleteTextAsync(AiTextRequest request, CancellationToken cancellationToken = default)
        {
            throw new AiProviderException("AI provider 'default' has no configured base URL.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset localNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return localNow.ToUniversalTime();
        }

    }
}
