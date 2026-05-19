using Notey.App.Setup;
using Notey.Core.Configuration;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;

namespace Notey.Tests;

public sealed class VaultBootstrapServiceTests : IDisposable
{
    private readonly List<string> tempDirectories = [];

    [Fact]
    public async Task BootstrapAsync_creates_owned_folders_and_fixed_structure()
    {
        var rootPath = CreateTempDirectory();
        var workspace = CreateWorkspace(rootPath);
        var service = new VaultBootstrapService(
            workspace,
            new FileSystemVaultEntityStore(workspace, new ObsidianLinkBuilder(workspace), TimeProvider.System));

        await service.BootstrapAsync(new VaultBootstrapRequest(
            Customers: ["Microsoft"],
            Projects: ["Apollo"],
            Topics: ["Roadmap"]),
            TestContext.Current.CancellationToken);
        await service.BootstrapAsync(new VaultBootstrapRequest(
            Customers: ["Microsoft"],
            Projects: ["Apollo"],
            Topics: ["Roadmap"]),
            TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(Path.Combine(rootPath, "Images")));
        Assert.True(Directory.Exists(Path.Combine(rootPath, "Notes", "Draft")));
        Assert.True(Directory.Exists(Path.Combine(rootPath, "People")));
        Assert.True(Directory.Exists(Path.Combine(rootPath, "Notes", "Customers", "Microsoft")));
        Assert.True(Directory.Exists(Path.Combine(rootPath, "Notes", "Projects", "Apollo")));
        Assert.True(Directory.Exists(Path.Combine(rootPath, "Notes", "Topics", "Roadmap")));
        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "Projects", "Apollo.md")));
        Assert.True(File.Exists(Path.Combine(rootPath, "Notes", "Topics", "Roadmap.md")));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(rootPath, "Notes", "Projects"), "Apollo*.md"));
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

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "notey-bootstrap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        tempDirectories.Add(path);
        return path;
    }

    private static FileSystemVaultWorkspace CreateWorkspace(string rootPath)
    {
        return new FileSystemVaultWorkspace(new NoteyOptions
        {
            Vault = new VaultOptions { RootPath = rootPath }
        });
    }
}
