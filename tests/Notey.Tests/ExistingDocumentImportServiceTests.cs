using Microsoft.Extensions.Logging.Abstractions;
using Notey.AI.Providers;
using Notey.App.Imports;
using Notey.App.Processing;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Ocr;
using Notey.Vault.Abstractions;
using Notey.Vault.Documents;
using Notey.Vault.Linking;
using Notey.Vault.Notes;

namespace Notey.Tests;

public sealed class ExistingDocumentImportServiceTests : IDisposable
{
    private readonly List<string> tempDirectories = [];

    [Fact]
    public async Task ImportFolderAsync_imports_text_referenced_assets_and_unknown_files()
    {
        var vaultRoot = CreateTempDirectory("notey-existing-import-vault");
        var sourceRoot = CreateTempDirectory("notey-existing-import-source");
        await WriteBytesAsync(Path.Combine(sourceRoot, "assets", "diagram.png"), [1, 2, 3]);
        await WriteBytesAsync(Path.Combine(sourceRoot, "archive.bin"), [4, 5, 6]);
        await WriteBytesAsync(Path.Combine(sourceRoot, "docs", "manual.pdf"), [7, 8, 9]);
        await WriteBytesAsync(Path.Combine(sourceRoot, "docs", "wiki.pdf"), [10, 11, 12]);
        await WriteTextAsync(Path.Combine(sourceRoot, "docs", "guide.md"), """
            # Guide

            A linked markdown note.
            """);
        await WriteTextAsync(Path.Combine(sourceRoot, "docs", "note.md"), """
            # Import note

            See ![diagram](../assets/diagram.png "diagram title").
            Read [manual](manual.pdf), [[wiki.pdf]], and [[guide.md]].
            """);
        var service = CreateService(vaultRoot);

        var result = await service.ImportFolderAsync(sourceRoot, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ImportedCount);
        Assert.Equal(3, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(File.Exists(Path.Combine(sourceRoot, "docs", "note.md")));
        Assert.True(File.Exists(Path.Combine(sourceRoot, "assets", "diagram.png")));
        Assert.True(File.Exists(Path.Combine(sourceRoot, "archive.bin")));
        Assert.True(File.Exists(Path.Combine(sourceRoot, "docs", "manual.pdf")));
        Assert.True(File.Exists(Path.Combine(sourceRoot, "docs", "wiki.pdf")));
        Assert.True(File.Exists(Path.Combine(sourceRoot, "docs", "guide.md")));
        var notePath = Path.Combine(vaultRoot, "Notes", "import note.md");
        Assert.True(File.Exists(notePath));
        var note = await File.ReadAllTextAsync(notePath, TestContext.Current.CancellationToken);
        Assert.Contains("Imported from: docs/note.md", note);
        Assert.Contains("![[Images/diagram.png]]", note);
        Assert.Contains("manual.pdf", note);
        Assert.Contains("wiki.pdf", note);
        Assert.Contains("[[Notes/guide.md|guide]]", note);
        Assert.True(File.Exists(Path.Combine(vaultRoot, "Images", "diagram.png")));
        Assert.Contains(
            Directory.EnumerateFiles(Path.Combine(vaultRoot, "Notes"), "manual.pdf", SearchOption.AllDirectories),
            static path => path.EndsWith($"{Path.DirectorySeparatorChar}manual.pdf", StringComparison.Ordinal));
        Assert.Contains(
            Directory.EnumerateFiles(Path.Combine(vaultRoot, "Notes"), "wiki.pdf", SearchOption.AllDirectories),
            static path => path.EndsWith($"{Path.DirectorySeparatorChar}wiki.pdf", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(vaultRoot, "Notes", "guide.md")));
        Assert.Contains(
            Directory.EnumerateFiles(Path.Combine(vaultRoot, "Notes"), "archive.bin", SearchOption.AllDirectories),
            static path => path.EndsWith($"{Path.DirectorySeparatorChar}archive.bin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportFolderAsync_reuses_image_imports_referenced_by_multiple_notes()
    {
        var vaultRoot = CreateTempDirectory("notey-existing-import-vault");
        var sourceRoot = CreateTempDirectory("notey-existing-import-source");
        await WriteBytesAsync(Path.Combine(sourceRoot, "assets", "diagram.png"), [1, 2, 3]);
        await WriteTextAsync(Path.Combine(sourceRoot, "first.md"), "# First\n\n![diagram](assets/diagram.png).");
        await WriteTextAsync(Path.Combine(sourceRoot, "second.md"), "# Second\n\n![diagram](assets/diagram.png).");
        var service = CreateService(vaultRoot);

        var result = await service.ImportFolderAsync(sourceRoot, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        var images = Directory.EnumerateFiles(Path.Combine(vaultRoot, "Images"), "diagram*.png").ToArray();
        Assert.Single(images);
        var first = await File.ReadAllTextAsync(Path.Combine(vaultRoot, "Notes", "first.md"), TestContext.Current.CancellationToken);
        var second = await File.ReadAllTextAsync(Path.Combine(vaultRoot, "Notes", "second.md"), TestContext.Current.CancellationToken);
        Assert.Contains("![[Images/diagram.png]]", first);
        Assert.Contains("![[Images/diagram.png]]", second);
    }

    [Fact]
    public async Task ProcessAsync_includes_import_context_in_ai_prompt()
    {
        var vaultRoot = CreateTempDirectory("notey-import-context");
        var provider = new RecordingAiProvider();
        var service = CreateDraftProcessingService(vaultRoot, provider);
        var draft = new NoteDraft(
            Path.Combine(vaultRoot, "Notes", "Draft", "draft.md"),
            "Imported body.",
            new DateTimeOffset(2026, 5, 19, 9, 0, 0, TimeSpan.Zero));
        Directory.CreateDirectory(Path.GetDirectoryName(draft.FilePath)!);
        await File.WriteAllTextAsync(draft.FilePath, draft.Content, TestContext.Current.CancellationToken);

        await service.ProcessAsync(
            draft,
            draft.Content,
            importContext: new DraftProcessingImportContext(
                "source-note.md",
                "Customers/Microsoft/source-note.md",
                new DateTimeOffset(2026, 5, 18, 10, 30, 0, TimeSpan.FromHours(2))),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("source file name: source-note.md", provider.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("source relative path: Customers/Microsoft/source-note.md", provider.LastPrompt, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var directory in tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private ExistingDocumentImportService CreateService(string rootPath)
    {
        var options = new NoteyOptions
        {
            Vault = new VaultOptions { RootPath = rootPath },
            Ai = new AiOptions { DefaultProviderId = "default" }
        };
        var workspace = new FileSystemVaultWorkspace(options);
        var linkBuilder = new ObsidianLinkBuilder(workspace);
        var noteDraftStore = new FileSystemNoteDraftStore(workspace, new NoteTemplateFactory(), new NoteFileNameGenerator());
        var fileImportService = new FileImportService(workspace, linkBuilder, new FakeMessageImportReader());
        var processingService = CreateDraftProcessingService(rootPath, aiProvider: null);
        return new ExistingDocumentImportService(
            noteDraftStore,
            fileImportService,
            processingService,
            workspace,
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 19, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<ExistingDocumentImportService>.Instance);
    }

    private static DraftProcessingService CreateDraftProcessingService(string rootPath, IAiProvider? aiProvider)
    {
        var options = new NoteyOptions
        {
            Vault = new VaultOptions { RootPath = rootPath },
            Ai = new AiOptions { DefaultProviderId = "default", ModelName = aiProvider is null ? string.Empty : "test" }
        };
        var workspace = new FileSystemVaultWorkspace(options);
        var providers = aiProvider is null ? [] : new[] { aiProvider };
        return new DraftProcessingService(
            options,
            workspace,
            new FileSystemDocumentStoreIndex(workspace),
            new AiProviderRegistry(providers, "default"),
            new RecordingOcrEngine(),
            new FixedTimeProvider(new DateTimeOffset(2026, 5, 19, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<DraftProcessingService>.Instance);
    }

    private string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        tempDirectories.Add(path);
        return path;
    }

    private static async Task WriteTextAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
    }

    private static async Task WriteBytesAsync(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
    }

    private sealed class FakeMessageImportReader : IMessageImportReader
    {
        public ValueTask<ImportedEmailMessage> ReadAsync(ImportFile file, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ImportedEmailMessage(
                Subject: "Imported message",
                From: null,
                To: null,
                Cc: null,
                SentOn: null,
                ReceivedOn: null,
                BodyText: "Imported message body.",
                BodyHtml: null,
                Attachments: [],
                EmbeddedMessages: []));
        }
    }

    private sealed class RecordingAiProvider : IAiProvider
    {
        public string Id => "default";

        public string LastPrompt { get; private set; } = string.Empty;

        public ValueTask<AiTextResponse> CompleteTextAsync(AiTextRequest request, CancellationToken cancellationToken = default)
        {
            LastPrompt = request.Prompt;
            return ValueTask.FromResult(new AiTextResponse(
                """{ "filename": "import-context-note", "body": "Imported body." }""",
                Id,
                "test"));
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

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
