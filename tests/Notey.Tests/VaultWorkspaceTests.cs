using Notey.Core.Configuration;
using Notey.Vault.Abstractions;

namespace Notey.Tests;

public sealed class VaultWorkspaceTests
{
    [Fact]
    public void GetPaths_normalizes_configured_vault_paths()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var options = new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = "Vault",
                NotesPath = "Notes",
                PeoplePath = "People",
                TopicsPath = "Topics",
                ProjectsPath = "Projects",
                ScreenshotPath = "Attachments/Snips"
            }
        };

        var workspace = new FileSystemVaultWorkspace(options);

        var paths = workspace.GetPaths();

        Assert.Equal(Path.Combine(documents, "Vault", "Notes"), paths.NotesPath);
        Assert.Equal(Path.Combine(documents, "Vault", "People"), paths.PeoplePath);
        Assert.Equal(Path.Combine(documents, "Vault", "Topics"), paths.TopicsPath);
        Assert.Equal(Path.Combine(documents, "Vault", "Projects"), paths.ProjectsPath);
        Assert.Equal(Path.Combine(documents, "Vault", "Attachments", "Snips"), paths.ScreenshotPath);
    }

    [Fact]
    public void GetPaths_resolves_defaults_against_stable_documents_location()
    {
        var workspace = new FileSystemVaultWorkspace(new NoteyOptions());

        var paths = workspace.GetPaths();

        Assert.Contains($"{Path.DirectorySeparatorChar}Notey{Path.DirectorySeparatorChar}", paths.NotesPath);
        Assert.EndsWith(Path.Combine("Notey", "Notes"), paths.NotesPath);
    }

    [Fact]
    public void GetPaths_rejects_empty_paths()
    {
        var options = new NoteyOptions
        {
            Vault = new VaultOptions
            {
                NotesPath = "",
                PeoplePath = "People",
                TopicsPath = "Topics",
                ProjectsPath = "Projects",
                ScreenshotPath = "Attachments/Snips"
            }
        };

        var workspace = new FileSystemVaultWorkspace(options);

        Assert.Throws<InvalidOperationException>(() => workspace.GetPaths());
    }

    [Fact]
    public void GetPaths_rejects_absolute_entity_paths_outside_vault_root()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "notey-vault");
        var options = new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = rootPath,
                NotesPath = "Notes",
                PeoplePath = Path.Combine(Path.GetTempPath(), "notey-people"),
                TopicsPath = "Topics",
                ProjectsPath = "Projects",
                ScreenshotPath = "Attachments/Snips"
            }
        };

        var workspace = new FileSystemVaultWorkspace(options);

        Assert.Throws<InvalidOperationException>(() => workspace.GetPaths());
    }
}
