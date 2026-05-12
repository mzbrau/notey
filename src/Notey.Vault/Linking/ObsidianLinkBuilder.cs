using Notey.Vault.Abstractions;

namespace Notey.Vault.Linking;

public sealed class ObsidianLinkBuilder(IVaultWorkspace workspace)
{
    private static readonly char[] CrossPlatformInvalidFileNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public string BuildWikiLink(VaultEntityKind kind, string displayName)
    {
        var linkPath = GetEntityLinkPath(kind, displayName);
        return FormatWikiLink(linkPath, NormalizeDisplayName(displayName));
    }

    public string GetEntityFilePath(VaultEntityKind kind, string displayName)
    {
        var paths = workspace.GetPaths();
        var folderPath = GetFolderPath(paths, kind);
        var fileName = $"{GetSafeFileStem(displayName)}.md";

        return Path.Combine(folderPath, fileName);
    }

    public string GetEntityLinkPath(VaultEntityKind kind, string displayName)
    {
        var paths = workspace.GetPaths();
        var filePath = GetEntityFilePath(kind, displayName);
        return GetLinkPath(paths, filePath);
    }

    public string GetLinkPath(VaultPaths paths, string filePath)
    {
        var relativePath = Path.GetRelativePath(paths.RootPath, Path.ChangeExtension(filePath, null));
        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public string BuildImageEmbed(string filePath)
    {
        var paths = workspace.GetPaths();
        var normalizedFilePath = Path.GetFullPath(filePath);
        var relativePath = Path.GetRelativePath(paths.RootPath, normalizedFilePath);
        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Vault image embeds must reference files inside the configured vault root.");
        }

        return $"![[{relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/')}]]";
    }

    public static string FormatWikiLink(string linkPath, string alias)
    {
        if (string.IsNullOrWhiteSpace(linkPath))
        {
            throw new ArgumentException("Obsidian link path cannot be empty.", nameof(linkPath));
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Obsidian link alias cannot be empty.", nameof(alias));
        }

        return $"[[{linkPath}|{EscapeAlias(alias)}]]";
    }

    public static string NormalizeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Vault entity names cannot be empty.", nameof(displayName));
        }

        return string.Join(' ', displayName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string GetSafeFileStem(string displayName)
    {
        var normalizedName = NormalizeDisplayName(displayName);
        var invalidCharacters = Path.GetInvalidFileNameChars()
            .Concat(CrossPlatformInvalidFileNameCharacters)
            .Distinct()
            .ToArray();
        var safeCharacters = normalizedName
            .Select(character => invalidCharacters.Contains(character) || char.IsControl(character) ? '-' : character)
            .ToArray();

        var safeName = new string(safeCharacters).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new ArgumentException("Vault entity name must contain at least one file-safe character.", nameof(displayName));
        }

        return safeName;
    }

    internal static string GetFolderPath(VaultPaths paths, VaultEntityKind kind)
    {
        return kind switch
        {
            VaultEntityKind.Person => paths.PeoplePath,
            VaultEntityKind.Topic => paths.TopicsPath,
            VaultEntityKind.Project => paths.ProjectsPath,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported vault entity kind.")
        };
    }

    internal static string GetKindLabel(VaultEntityKind kind)
    {
        return kind switch
        {
            VaultEntityKind.Person => "person",
            VaultEntityKind.Topic => "topic",
            VaultEntityKind.Project => "project",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported vault entity kind.")
        };
    }

    private static string EscapeAlias(string alias)
    {
        return alias.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
