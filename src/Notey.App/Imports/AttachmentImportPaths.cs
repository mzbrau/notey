using System.Text;
using System.Text.RegularExpressions;
using Notey.Vault.Abstractions;

namespace Notey.App.Imports;

public static partial class AttachmentImportPaths
{
    private static readonly HashSet<string> SupportedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".webp",
            ".bmp",
            ".svg"
        };

    private static readonly char[] CrossPlatformInvalidFileNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly char[] ObsidianReservedFileNameCharacters = ['#', '^', '[', ']'];

    public static bool IsSupportedImageFileName(string fileName)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(fileName));
    }

    public static string GetDraftAssetsDirectory(string draftFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftFilePath);

        var directory = Path.GetDirectoryName(draftFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Draft file path must include a directory.");
        }

        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(draftFilePath)}.assets");
    }

    public static string GetFinalAssetsDirectory(string noteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noteFilePath);

        var directory = Path.GetDirectoryName(noteFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Final note file path must include a directory.");
        }

        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(noteFilePath)}.assets");
    }

    public static string GetSafeFileName(string fileName)
    {
        var originalName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName);
        var extension = Path.GetExtension(originalName);
        var stem = Path.GetFileNameWithoutExtension(originalName);
        var safeStem = GetSafeFileStem(stem);
        var safeExtension = GetSafeExtension(extension);

        return string.IsNullOrWhiteSpace(safeExtension)
            ? safeStem
            : $"{safeStem}{safeExtension}";
    }

    public static string GetUniqueFilePath(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var safeFileName = GetSafeFileName(fileName);
        var fileStem = Path.GetFileNameWithoutExtension(safeFileName);
        var extension = Path.GetExtension(safeFileName);
        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            var candidateName = suffix == 1 ? safeFileName : $"{fileStem}-{suffix}{extension}";
            var candidatePath = Path.Combine(directory, candidateName);
            if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique import filename.");
    }

    public static string BuildAttachmentLink(VaultPaths paths, string filePath, string? alias = null)
    {
        var relativePath = GetVaultRelativePath(paths, filePath);
        var display = string.IsNullOrWhiteSpace(alias) ? Path.GetFileName(filePath) : alias.Trim();
        return string.IsNullOrWhiteSpace(display)
            ? $"[[{relativePath}]]"
            : $"[[{relativePath}|{EscapeAlias(display)}]]";
    }

    public static string GetVaultRelativePath(VaultPaths paths, string filePath)
    {
        var normalizedFilePath = Path.GetFullPath(filePath);
        var relativePath = Path.GetRelativePath(paths.RootPath, normalizedFilePath);
        if (IsOutside(relativePath))
        {
            throw new InvalidOperationException("Imported files must stay inside the configured vault root.");
        }

        return NormalizeVaultRelativePath(relativePath);
    }

    public static string? TryResolveVaultRelativePath(VaultPaths paths, string vaultRelativePath)
    {
        var normalizedRelativePath = vaultRelativePath.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(paths.RootPath, normalizedRelativePath));
        var relativePath = Path.GetRelativePath(paths.RootPath, candidate);
        return IsOutside(relativePath) ? null : candidate;
    }

    public static string NormalizeVaultRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public static string ToReferenceText(string markdown)
    {
        var normalized = markdown.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : $"- {normalized}";
    }

    private static string GetSafeFileStem(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var invalidCharacters = Path.GetInvalidFileNameChars()
            .Concat(CrossPlatformInvalidFileNameCharacters)
            .Concat(ObsidianReservedFileNameCharacters)
            .Distinct()
            .ToArray();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (invalidCharacters.Contains(character) || char.IsControl(character))
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(character);
            }
        }

        var safe = RepeatedDashRegex().Replace(builder.ToString(), "-").Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(safe) ? "attachment" : safe;
    }

    private static string GetSafeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var safe = new string(extension
            .Where(static character => character == '.' || char.IsAsciiLetterOrDigit(character))
            .ToArray());
        return safe.StartsWith(".", StringComparison.Ordinal) && safe.Length > 1 ? safe : string.Empty;
    }

    private static string EscapeAlias(string alias)
    {
        return alias.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static bool IsOutside(string relativePath)
    {
        return relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativePath);
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex RepeatedDashRegex();
}
