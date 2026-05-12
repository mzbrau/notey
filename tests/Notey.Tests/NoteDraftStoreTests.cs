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
    public async Task OpenAsync_reads_existing_note_draft()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);
        var draft = await store.CreateAsync(new DateTimeOffset(2026, 5, 11, 22, 45, 30, TimeSpan.Zero));

        var openedDraft = await store.OpenAsync(draft.FilePath);

        Assert.Equal(draft.FilePath, openedDraft.FilePath);
        Assert.Equal(draft.Content, openedDraft.Content);
        Assert.Equal(draft.CreatedAt, openedDraft.CreatedAt);
    }

    [Fact]
    public async Task FindMostRecentAsync_returns_latest_note_created_within_window()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);
        _ = await store.CreateAsync(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var olderRecent = await store.CreateAsync(new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero));
        var latestRecent = await store.CreateAsync(new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero));

        var recent = await store.FindMostRecentAsync(new DateTimeOffset(2026, 5, 8, 8, 0, 0, TimeSpan.Zero));

        Assert.NotNull(recent);
        Assert.Equal(latestRecent.FilePath, recent.FilePath);
        Assert.NotEqual(olderRecent.FilePath, recent.FilePath);
    }

    [Fact]
    public async Task FindMostRecentAsync_returns_null_when_notes_are_outside_window()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);
        _ = await store.CreateAsync(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero));

        var recent = await store.FindMostRecentAsync(new DateTimeOffset(2026, 5, 8, 8, 0, 0, TimeSpan.Zero));

        Assert.Null(recent);
    }

    [Fact]
    public async Task OpenAsync_rejects_paths_outside_notes_folder()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);
        var outsidePath = Path.Combine(rootPath, "outside.md");
        await File.WriteAllTextAsync(outsidePath, "# Outside");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenAsync(outsidePath));
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

    [Fact]
    public async Task OpenAsync_does_not_read_created_from_note_body()
    {
        var rootPath = CreateTempDirectory();
        var notesPath = Path.Combine(rootPath, "Notes");
        Directory.CreateDirectory(notesPath);
        var filePath = Path.Combine(notesPath, "test.md");
        var expectedCreatedAt = new DateTimeOffset(2026, 5, 11, 22, 45, 30, TimeSpan.Zero);
        await File.WriteAllTextAsync(filePath, $"""
            ---
            created: {expectedCreatedAt:O}
            ---

            # My note

            created: 1999-01-01T00:00:00.0000000+00:00
            """);
        var store = CreateStore(rootPath);

        var draft = await store.OpenAsync(filePath);

        Assert.Equal(expectedCreatedAt, draft.CreatedAt);
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
