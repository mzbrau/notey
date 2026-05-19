using Notey.Core.Configuration;
using Notey.Vault.Abstractions;
using Notey.Vault.Documents;

namespace Notey.Tests;

public sealed class DocumentStoreIndexTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task GetFolderCommandsAsync_maps_notes_folders_to_singular_commands()
    {
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Customers"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Companies"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Draft"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Meetings"));
        var index = CreateIndex(rootPath);

        var commands = await index.GetFolderCommandsAsync();

        Assert.Collection(
            commands,
            command =>
            {
                Assert.Equal("company", command.CommandName);
                Assert.Equal("Companies", command.FolderName);
            },
            command =>
            {
                Assert.Equal("customer", command.CommandName);
                Assert.Equal("Customers", command.FolderName);
            });
    }

    [Fact]
    public async Task GetTopicSuggestionsAsync_reads_documents_excluding_draft_meetings_and_tasks()
    {
        var rootPath = CreateTempDirectory();
        await WriteAsync(rootPath, "Notes/accounts.md", "# Accounts");
        await WriteAsync(rootPath, "Notes/Customers/Microsoft/contracts.md", "# Contracts");
        await WriteAsync(rootPath, "Notes/Customers/Microsoft/Meetings/2026-05-13 - accounts.md", "# Meeting");
        await WriteAsync(rootPath, "Notes/Draft/2026-05-13-note.md", "# Draft");
        await WriteAsync(rootPath, "Notes/tasks.md", "# Tasks");
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Topics", "Meetings"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Topics", "Roadmap"));
        var index = CreateIndex(rootPath);

        var suggestions = await index.GetTopicSuggestionsAsync();

        Assert.Equal(["accounts", "contracts", "Meetings", "Roadmap"], suggestions.Select(static suggestion => suggestion.Title));
    }

    [Fact]
    public async Task GetDynamicValueSuggestionsAsync_reads_folder_values_for_command()
    {
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Customers", "Microsoft"));
        await WriteAsync(rootPath, "Notes/Customers/Atlassian.md", "# Atlassian");
        Directory.CreateDirectory(Path.Combine(rootPath, "Notes", "Customers", "Meetings"));
        var index = CreateIndex(rootPath);

        var suggestions = await index.GetDynamicValueSuggestionsAsync("customer");

        Assert.Equal(["Atlassian", "Microsoft"], suggestions.Select(static suggestion => suggestion.Value));
    }

    private static FileSystemDocumentStoreIndex CreateIndex(string rootPath)
    {
        return new FileSystemDocumentStoreIndex(new FileSystemVaultWorkspace(new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = rootPath
            }
        }));
    }

    private static async Task WriteAsync(string rootPath, string relativePath, string content)
    {
        var path = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "notey-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
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
}
