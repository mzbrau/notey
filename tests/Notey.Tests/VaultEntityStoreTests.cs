using Notey.Core.Configuration;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;

namespace Notey.Tests;

public sealed class VaultEntityStoreTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task EnsureAsync_creates_missing_person_document()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);

        var entity = await store.EnsureAsync(VaultEntityKind.Person, "Jane Doe");

        Assert.Equal("Jane Doe", entity.Name);
        Assert.Equal("People/Jane Doe", entity.LinkPath);
        Assert.True(File.Exists(Path.Combine(rootPath, "People", "Jane Doe.md")));
        Assert.Contains("type: person", await File.ReadAllTextAsync(entity.FilePath));
    }

    [Fact]
    public async Task EnsureAsync_quotes_yaml_title_values()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);

        var entity = await store.EnsureAsync(VaultEntityKind.Topic, "Release #1");

        Assert.Contains("title: \"Release #1\"", await File.ReadAllTextAsync(entity.FilePath));
    }

    [Fact]
    public async Task EnsureAsync_reuses_escaped_yaml_title_match()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);

        var first = await store.EnsureAsync(VaultEntityKind.Person, "Jane \"JJ\" Doe");
        var second = await store.EnsureAsync(VaultEntityKind.Person, "Jane \"JJ\" Doe");

        Assert.Equal(first.FilePath, second.FilePath);
        Assert.Equal("Jane \"JJ\" Doe", second.Name);
    }

    [Fact]
    public async Task EnsureAsync_uses_suffix_when_sanitized_file_name_collides_with_different_entity()
    {
        var rootPath = CreateTempDirectory();
        var topicsPath = Path.Combine(rootPath, "Notes", "Topics");
        Directory.CreateDirectory(topicsPath);
        await File.WriteAllTextAsync(Path.Combine(topicsPath, "Roadmap- Q2.md"), """
            ---
            title: Existing Roadmap
            aliases: []
            ---

            # Existing Roadmap
            """);
        var store = CreateStore(rootPath);

        var entity = await store.EnsureAsync(VaultEntityKind.Topic, "Roadmap: Q2");

        Assert.Equal("Roadmap- Q2-2.md", Path.GetFileName(entity.FilePath));
        Assert.Equal("Notes/Topics/Roadmap- Q2-2", entity.LinkPath);
    }

    [Fact]
    public async Task EnsureAsync_reuses_sanitized_collision_when_existing_title_matches()
    {
        var rootPath = CreateTempDirectory();
        var topicsPath = Path.Combine(rootPath, "Notes", "Topics");
        Directory.CreateDirectory(topicsPath);
        var existingPath = Path.Combine(topicsPath, "Roadmap- Q2.md");
        await File.WriteAllTextAsync(existingPath, """
            ---
            title: Roadmap: Q2
            aliases: []
            ---

            # Roadmap: Q2
            """);
        var store = CreateStore(rootPath);

        var entity = await store.EnsureAsync(VaultEntityKind.Topic, "Roadmap: Q2");

        Assert.Equal(existingPath, entity.FilePath);
    }

    [Fact]
    public async Task GetAllAsync_reads_titles_and_aliases_from_frontmatter()
    {
        var rootPath = CreateTempDirectory();
        var peoplePath = Path.Combine(rootPath, "People");
        Directory.CreateDirectory(peoplePath);
        await File.WriteAllTextAsync(Path.Combine(peoplePath, "michael.md"), """
            ---
            title: Michael Browne
            aliases: [Mike, mb]
            ---

            # Michael Browne
            """);
        var store = CreateStore(rootPath);

        var people = await store.GetAllAsync(VaultEntityKind.Person);

        var person = Assert.Single(people);
        Assert.Equal("Michael Browne", person.Name);
        Assert.Equal(["Mike", "mb"], person.Aliases);
    }

    [Fact]
    public async Task EnsureAsync_reuses_existing_alias_match()
    {
        var rootPath = CreateTempDirectory();
        var peoplePath = Path.Combine(rootPath, "People");
        Directory.CreateDirectory(peoplePath);
        var existingPath = Path.Combine(peoplePath, "Michael Browne.md");
        await File.WriteAllTextAsync(existingPath, """
            ---
            title: Michael Browne
            aliases:
              - Mike
            ---

            # Michael Browne
            """);
        var store = CreateStore(rootPath);

        var entity = await store.EnsureAsync(VaultEntityKind.Person, "Mike");

        Assert.Equal(existingPath, entity.FilePath);
        Assert.Single(Directory.EnumerateFiles(peoplePath, "*.md"));
    }

    [Fact]
    public async Task EnsureAsync_reuses_existing_person_when_two_part_name_order_differs()
    {
        var rootPath = CreateTempDirectory();
        var peoplePath = Path.Combine(rootPath, "People");
        Directory.CreateDirectory(peoplePath);
        var existingPath = Path.Combine(peoplePath, "Michael Browne.md");
        await File.WriteAllTextAsync(existingPath, """
            ---
            title: Michael Browne
            aliases: []
            ---

            # Michael Browne
            """);
        var store = CreateStore(rootPath);

        var commaOrdered = await store.EnsureAsync(VaultEntityKind.Person, "Browne, Michael");
        var reversed = await store.EnsureAsync(VaultEntityKind.Person, "Browne Michael");

        Assert.Equal(existingPath, commaOrdered.FilePath);
        Assert.Equal(existingPath, reversed.FilePath);
        Assert.Single(Directory.EnumerateFiles(peoplePath, "*.md"));
    }

    [Fact]
    public async Task EnsureAsync_reuses_existing_person_when_multi_part_name_order_differs()
    {
        var rootPath = CreateTempDirectory();
        var peoplePath = Path.Combine(rootPath, "People");
        Directory.CreateDirectory(peoplePath);
        var existingPath = Path.Combine(peoplePath, "Jane Marie Doe.md");
        await File.WriteAllTextAsync(existingPath, """
            ---
            title: Jane Marie Doe
            aliases: []
            ---

            # Jane Marie Doe
            """);
        var store = CreateStore(rootPath);

        var commaOrdered = await store.EnsureAsync(VaultEntityKind.Person, "Doe, Jane Marie");
        var reversed = await store.EnsureAsync(VaultEntityKind.Person, "Doe Jane Marie");

        Assert.Equal(existingPath, commaOrdered.FilePath);
        Assert.Equal(existingPath, reversed.FilePath);
        Assert.Single(Directory.EnumerateFiles(peoplePath, "*.md"));
    }

    [Fact]
    public async Task EnsureAsync_allows_concurrent_person_creation_for_same_name()
    {
        var rootPath = CreateTempDirectory();
        var store = CreateStore(rootPath);

        var created = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.EnsureAsync(VaultEntityKind.Person, "Jane Doe")));

        Assert.All(created, entity => Assert.Equal("Jane Doe", entity.Name));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(rootPath, "People"), "*.md"));
    }

    private static FileSystemVaultEntityStore CreateStore(string rootPath)
    {
        var workspace = CreateWorkspace(rootPath);
        var linkBuilder = new ObsidianLinkBuilder(workspace);

        return new FileSystemVaultEntityStore(workspace, linkBuilder, TimeProvider.System);
    }

    private static FileSystemVaultWorkspace CreateWorkspace(string rootPath)
    {
        return new FileSystemVaultWorkspace(new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = rootPath
            }
        });
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
