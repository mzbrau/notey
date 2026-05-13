using Notey.AI.Providers;
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
        var service = CreateService(rootPath, """{ "body": "Keep the accounts safe.", "people": ["Jane Doe"], "tags": ["accounts"] }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/meeting\n/customer Microsoft\n/topic Accounts\n\nKeep accounts safe.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        var result = await service.ProcessAsync(draft, draft.Content);

        Assert.True(result.Processed);
        var target = Path.Combine(rootPath, "Notes", "Customers", "Microsoft", "Meetings", "2026-05-13 - accounts.md");
        Assert.True(File.Exists(target));
        var content = await File.ReadAllTextAsync(target);
        Assert.Contains("meeting: true", content);
        Assert.Contains("customer: \"Microsoft\"", content);
        Assert.Contains("topic: \"Accounts\"", content);
        Assert.Contains("Keep the accounts safe.", content);
        Assert.False(File.Exists(draft.FilePath));
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
        var service = CreateService(rootPath, """{ "body": "New note.", "tags": ["new"] }""");
        var draft = new NoteDraft(Path.Combine(rootPath, "Notes", "Draft", "draft.md"), "/topic Accounts\n\nRaw.", new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero));
        await WriteFileAsync(draft.FilePath, draft.Content);

        await service.ProcessAsync(draft, draft.Content);

        var content = await File.ReadAllTextAsync(target);
        Assert.Single(FindAll(content, "## 2026-05-13"));
        Assert.Contains("Earlier note.", content);
        Assert.Contains("New note.", content);
        Assert.Contains("  - \"#old\"", content);
        Assert.Contains("  - \"#new\"", content);
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

    private DraftProcessingService CreateService(string rootPath, string aiResponse)
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
            new AiProviderRegistry([new RecordingAiProvider(aiResponse)], "default"),
            new RecordingOcrEngine(),
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero)));
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

    private sealed class RecordingAiProvider(string response) : IAiProvider
    {
        public string Id => "default";

        public ValueTask<AiTextResponse> CompleteTextAsync(AiTextRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new AiTextResponse(response, Id, "test"));
        }
    }

    private sealed class RecordingOcrEngine : ITesseractOcrEngine
    {
        public ValueTask<OcrResult> RecognizeAsync(TesseractOcrRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new OcrResult("ocr text", "eng", 1.0, []));
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
