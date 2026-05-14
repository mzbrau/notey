using Notey.Vault.Abstractions;

namespace Notey.Vault.Documents;

public sealed class FileSystemDocumentStoreIndex(IVaultWorkspace workspace) : IDocumentStoreIndex
{
    private static readonly HashSet<string> ExcludedTopicFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft",
        "Meetings"
    };

    public Task<IReadOnlyList<VaultFolderCommand>> GetFolderCommandsAsync(CancellationToken cancellationToken = default)
    {
        var paths = workspace.GetPaths();
        if (!Directory.Exists(paths.NotesPath))
        {
            return Task.FromResult<IReadOnlyList<VaultFolderCommand>>([]);
        }

        var commands = Directory
            .EnumerateDirectories(paths.NotesPath, "*", SearchOption.TopDirectoryOnly)
            .Select(directory =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return directory;
            })
            .Where(static directory => !ExcludedTopicFolderNames.Contains(Path.GetFileName(directory)))
            .Select(static directory =>
            {
                var folderName = Path.GetFileName(directory);
                return new VaultFolderCommand(ToCommandName(folderName), folderName, directory);
            })
            .Where(static command => !string.IsNullOrWhiteSpace(command.CommandName))
            .OrderBy(static command => command.CommandName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<VaultFolderCommand>>(commands);
    }

    public Task<IReadOnlyList<VaultDocumentSuggestion>> GetTopicSuggestionsAsync(CancellationToken cancellationToken = default)
    {
        var paths = workspace.GetPaths();
        if (!Directory.Exists(paths.NotesPath))
        {
            return Task.FromResult<IReadOnlyList<VaultDocumentSuggestion>>([]);
        }

        var suggestions = Directory
            .EnumerateFiles(paths.NotesPath, "*.md", SearchOption.AllDirectories)
            .Select(filePath =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return filePath;
            })
            .Where(filePath => !IsUnderExcludedTopicFolder(paths, filePath))
            .Where(static filePath => !string.Equals(Path.GetFileName(filePath), "tasks.md", StringComparison.OrdinalIgnoreCase))
            .Select(filePath => new VaultDocumentSuggestion(
                Path.GetFileNameWithoutExtension(filePath),
                filePath,
                GetRelativeMarkdownPath(paths, filePath)))
            .OrderBy(static suggestion => suggestion.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<VaultDocumentSuggestion>>(suggestions);
    }

    public async Task<IReadOnlyList<VaultDynamicValueSuggestion>> GetDynamicValueSuggestionsAsync(
        string commandName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);

        var command = (await GetFolderCommandsAsync(cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.CommandName, commandName.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
        if (command is null || !Directory.Exists(command.FolderPath))
        {
            return [];
        }

        var folders = Directory
            .EnumerateDirectories(command.FolderPath, "*", SearchOption.TopDirectoryOnly)
            .Select(directory =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return directory;
            })
            .Where(static directory => !string.Equals(Path.GetFileName(directory), "Meetings", StringComparison.OrdinalIgnoreCase))
            .Select(static directory => new VaultDynamicValueSuggestion(Path.GetFileName(directory), directory, true));
        var documents = Directory
            .EnumerateFiles(command.FolderPath, "*.md", SearchOption.TopDirectoryOnly)
            .Select(filePath =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return filePath;
            })
            .Where(static filePath => !string.Equals(Path.GetFileName(filePath), "tasks.md", StringComparison.OrdinalIgnoreCase))
            .Select(static filePath => new VaultDynamicValueSuggestion(Path.GetFileNameWithoutExtension(filePath), filePath, false));

        return folders
            .Concat(documents)
            .GroupBy(static suggestion => suggestion.Value, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static suggestion => suggestion.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ToCommandName(string folderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        var normalized = new string(folderName
            .Trim()
            .Where(static character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Select(static character => char.ToLowerInvariant(character))
            .ToArray());

        if (normalized.EndsWith("ies", StringComparison.Ordinal) && normalized.Length > 3)
        {
            return normalized[..^3] + "y";
        }

        if (normalized.EndsWith('s') && !normalized.EndsWith("ss", StringComparison.Ordinal) && normalized.Length > 1)
        {
            return normalized[..^1];
        }

        return normalized;
    }

    private static bool IsUnderExcludedTopicFolder(VaultPaths paths, string filePath)
    {
        var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(paths.NotesPath, filePath));
        if (string.IsNullOrWhiteSpace(relativeDirectory))
        {
            return false;
        }

        return relativeDirectory
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => ExcludedTopicFolderNames.Contains(segment));
    }

    private static string GetRelativeMarkdownPath(VaultPaths paths, string filePath)
    {
        return Path.GetRelativePath(paths.RootPath, Path.ChangeExtension(filePath, null))
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
