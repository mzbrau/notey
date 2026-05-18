using Notey.App.Imports;
using Notey.Core.Configuration;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;

namespace Notey.Tests;

public sealed class FileImportServiceTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task ImportAsync_copies_images_to_images_folder_and_returns_embed()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath);
        var draftPath = Path.Combine(rootPath, "Notes", "Draft", "draft.md");

        var result = await service.ImportAsync(
            [ImportFile.FromBytes("photo.png", [1, 2, 3])],
            FileImportContext.ForDraft(draftPath),
            TestContext.Current.CancellationToken);

        var imagePath = Path.Combine(rootPath, "Images", "photo.png");
        Assert.True(File.Exists(imagePath));
        Assert.Equal("![[Images/photo.png]]", result.Markdown);
        Assert.Equal([imagePath], result.WrittenPaths);
    }

    [Fact]
    public async Task ImportAsync_stages_generic_files_with_active_draft()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath);
        var draftPath = Path.Combine(rootPath, "Notes", "Draft", "2026-05-18-note.md");

        var result = await service.ImportAsync(
            [ImportFile.FromBytes("spec#[draft]?.pdf", [1, 2, 3])],
            FileImportContext.ForDraft(draftPath),
            TestContext.Current.CancellationToken);

        var attachmentPath = Path.Combine(rootPath, "Notes", "Draft", "2026-05-18-note.assets", "spec-draft.pdf");
        Assert.True(File.Exists(attachmentPath));
        Assert.Equal("[[Notes/Draft/2026-05-18-note.assets/spec-draft.pdf|spec-draft.pdf]]", result.Markdown);
        Assert.Equal([attachmentPath], result.WrittenPaths);
    }

    [Fact]
    public async Task ImportAsync_copies_generic_files_directly_to_open_final_note_assets()
    {
        var rootPath = CreateTempDirectory();
        var service = CreateService(rootPath);
        var finalNotePath = Path.Combine(rootPath, "Notes", "roadmap.md");

        var result = await service.ImportAsync(
            [ImportFile.FromBytes("brief.docx", [4, 5, 6])],
            FileImportContext.ForFinalNote(finalNotePath),
            TestContext.Current.CancellationToken);

        var attachmentPath = Path.Combine(rootPath, "Notes", "roadmap.assets", "brief.docx");
        Assert.True(File.Exists(attachmentPath));
        Assert.Equal("[[Notes/roadmap.assets/brief.docx|brief.docx]]", result.Markdown);
        Assert.Equal([attachmentPath], result.WrittenPaths);
    }

    [Fact]
    public async Task ImportAsync_formats_msg_and_recursively_imports_non_inline_attachments()
    {
        var rootPath = CreateTempDirectory();
        var draftPath = Path.Combine(rootPath, "Notes", "Draft", "draft.md");
        var message = new ImportedEmailMessage(
            "Project update",
            "Jane Doe <jane@example.com>",
            "Team <team@example.com>",
            "Manager <manager@example.com>",
            new DateTimeOffset(2026, 5, 18, 9, 30, 0, TimeSpan.FromHours(2)),
            null,
            "Hello team,\n\nPlease review the attached agenda.",
            null,
            [
                new ImportedEmailAttachment("diagram.png", [1, 2], IsInline: false, ContentId: null, MimeType: "image/png"),
                new ImportedEmailAttachment("agenda.docx", [3, 4], IsInline: false, ContentId: null, MimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
                new ImportedEmailAttachment("logo.png", [5, 6], IsInline: true, ContentId: "logo", MimeType: "image/png")
            ],
            [
                new ImportedEmailMessage(
                    "Forwarded detail",
                    "Alex <alex@example.com>",
                    null,
                    null,
                    null,
                    null,
                    "Forwarded body.",
                    null,
                    [new ImportedEmailAttachment("thread.txt", [7, 8], IsInline: false, ContentId: null, MimeType: "text/plain")],
                    [])
            ]);
        var service = CreateService(rootPath, new FakeMessageImportReader(message));

        var result = await service.ImportAsync(
            [ImportFile.FromBytes("message.msg", [9])],
            FileImportContext.ForDraft(draftPath),
            TestContext.Current.CancellationToken);

        Assert.Contains("## Email: Project update", result.Markdown);
        Assert.Contains("| From | Jane Doe <jane@example.com> |", result.Markdown);
        Assert.Contains("| To | Team <team@example.com> |", result.Markdown);
        Assert.Contains("| Cc | Manager <manager@example.com> |", result.Markdown);
        Assert.Contains("| Sent | 2026-05-18 09:30 +02:00 |", result.Markdown);
        Assert.Contains("Hello team,", result.Markdown);
        Assert.Contains("- ![[Images/diagram.png]]", result.Markdown);
        Assert.Contains("- [[Notes/Draft/draft.assets/agenda.docx|agenda.docx]]", result.Markdown);
        Assert.Contains("### Email: Forwarded detail", result.Markdown);
        Assert.Contains("- [[Notes/Draft/draft.assets/thread.txt|thread.txt]]", result.Markdown);
        Assert.DoesNotContain("logo.png", result.Markdown);
        Assert.True(File.Exists(Path.Combine(rootPath, "Images", "diagram.png")));
        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "Draft", "draft.assets", "agenda.docx")));
        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "Draft", "draft.assets", "thread.txt")));
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

    private FileImportService CreateService(string rootPath, IMessageImportReader? messageImportReader = null)
    {
        var options = new NoteyOptions
        {
            Vault = new VaultOptions { RootPath = rootPath }
        };
        var workspace = new FileSystemVaultWorkspace(options);
        return new FileImportService(
            workspace,
            new ObsidianLinkBuilder(workspace),
            messageImportReader ?? new FakeMessageImportReader(EmptyMessage));
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "notey-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static ImportedEmailMessage EmptyMessage { get; } = new(
        null,
        null,
        null,
        null,
        null,
        null,
        string.Empty,
        null,
        [],
        []);

    private sealed class FakeMessageImportReader(ImportedEmailMessage message) : IMessageImportReader
    {
        public ValueTask<ImportedEmailMessage> ReadAsync(ImportFile file, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(message);
        }
    }
}
