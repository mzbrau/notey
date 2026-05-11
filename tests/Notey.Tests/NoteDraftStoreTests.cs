using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Vault.Abstractions;
using Notey.Vault.Notes;

namespace Notey.Tests;

public sealed class NoteDraftStoreTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task CreateAsync_writes_obsidian_template_to_configured_notes_folder()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);
        var createdAt = new DateTimeOffset(2026, 5, 11, 22, 45, 30, TimeSpan.FromHours(2));

        var draft = await store.CreateAsync(createdAt);

        Assert.Equal(Path.Combine(rootPath, "Notes", "2026-05-11-224530-note.md"), draft.FilePath);
        Assert.True(File.Exists(draft.FilePath));
        Assert.Contains("created: 2026-05-11T22:45:30.0000000+02:00", draft.Content);
        Assert.Contains("# Untitled note", await File.ReadAllTextAsync(draft.FilePath));
    }

    [Fact]
    public async Task SaveAsync_updates_existing_draft_file()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);
        var draft = await store.CreateAsync(new DateTimeOffset(2026, 5, 11, 22, 45, 30, TimeSpan.Zero));

        await store.SaveAsync(draft, "# Updated note");

        Assert.Equal("# Updated note", await File.ReadAllTextAsync(draft.FilePath));
    }

    [Fact]
    public async Task CreateAsync_uses_suffix_when_timestamp_filename_already_exists()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);
        var createdAt = new DateTimeOffset(2026, 5, 11, 22, 45, 30, TimeSpan.Zero);

        _ = await store.CreateAsync(createdAt);
        var secondDraft = await store.CreateAsync(createdAt);

        Assert.EndsWith("2026-05-11-224530-note-2.md", secondDraft.FilePath);
    }

    private static FileSystemNoteDraftStore CreateStore(string rootPath)
    {
        var options = new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = rootPath,
                NotesPath = "Notes",
                PeoplePath = "People",
                TopicsPath = "Topics",
                ProjectsPath = "Projects",
                ScreenshotPath = "Attachments/Snips"
            }
        };

        return new FileSystemNoteDraftStore(
            new FileSystemVaultWorkspace(options),
            new NoteTemplateFactory(),
            new NoteFileNameGenerator());
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

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "notey-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
