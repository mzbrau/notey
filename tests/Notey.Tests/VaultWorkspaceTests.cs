using Notey.Core.Configuration;
using Notey.Vault.Abstractions;

namespace Notey.Tests;

public sealed class VaultWorkspaceTests
{
    [Fact]
    public void GetPaths_derives_owned_vault_paths()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var workspace = new FileSystemVaultWorkspace(new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = "Vault"
            }
        });

        var paths = workspace.GetPaths();

        Assert.Equal(Path.Combine(documents, "Vault"), paths.RootPath);
        Assert.Equal(Path.Combine(documents, "Vault", "Images"), paths.ImagesPath);
        Assert.Equal(Path.Combine(documents, "Vault", "Notes"), paths.NotesPath);
        Assert.Equal(Path.Combine(documents, "Vault", "Notes", "Draft"), paths.DraftPath);
        Assert.Equal(Path.Combine(documents, "Vault", "People"), paths.PeoplePath);
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
    public void GetPaths_derives_owned_paths_from_absolute_root()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "notey-vault");
        var workspace = new FileSystemVaultWorkspace(new NoteyOptions
        {
            Vault = new VaultOptions
            {
                RootPath = rootPath
            }
        });

        var paths = workspace.GetPaths();

        Assert.Equal(Path.Combine(rootPath, "Images"), paths.ImagesPath);
        Assert.Equal(Path.Combine(rootPath, "Notes"), paths.NotesPath);
        Assert.Equal(Path.Combine(rootPath, "Notes", "Draft"), paths.DraftPath);
        Assert.Equal(Path.Combine(rootPath, "People"), paths.PeoplePath);
    }
}
