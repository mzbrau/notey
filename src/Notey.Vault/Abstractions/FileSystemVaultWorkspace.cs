using Notey.Core.Configuration;

namespace Notey.Vault.Abstractions;

public sealed class FileSystemVaultWorkspace(NoteyOptions options) : IVaultWorkspace
{
    public VaultPaths GetPaths()
    {
        var vault = options.Vault;
        var rootPath = ResolveRootPath(vault.RootPath);

        return new VaultPaths(
            Normalize(rootPath, vault.NotesPath),
            Normalize(rootPath, vault.PeoplePath),
            Normalize(rootPath, vault.TopicsPath),
            Normalize(rootPath, vault.ProjectsPath),
            Normalize(rootPath, vault.ScreenshotPath));
    }

    private static string ResolveRootPath(string rootPath)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return Path.Combine(documents, "Notey");
        }

        return Path.IsPathFullyQualified(rootPath)
            ? Path.GetFullPath(rootPath)
            : Path.GetFullPath(rootPath, documents);
    }

    private static string Normalize(string rootPath, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Vault paths must be configured before notes can be saved.");
        }

        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, rootPath);
    }
}
