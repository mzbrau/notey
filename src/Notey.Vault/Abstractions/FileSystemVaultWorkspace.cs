using Notey.Core.Configuration;

namespace Notey.Vault.Abstractions;

public sealed class FileSystemVaultWorkspace(NoteyOptions options) : IVaultWorkspace
{
    public VaultPaths GetPaths()
    {
        var vault = options.Vault;
        var rootPath = ResolveRootPath(vault.RootPath);

        return new VaultPaths(
            rootPath,
            Normalize(rootPath, "Images"),
            Normalize(rootPath, "Notes"),
            Normalize(rootPath, Path.Combine("Notes", "Draft")),
            Normalize(rootPath, "People"));
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

        var normalizedPath = Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, rootPath);

        var relativePath = Path.GetRelativePath(rootPath, normalizedPath);
        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Vault paths must stay within the configured vault root.");
        }

        return normalizedPath;
    }
}
