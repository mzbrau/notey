namespace Notey.Vault.Documents;

public interface IDocumentStoreIndex
{
    Task<IReadOnlyList<VaultFolderCommand>> GetFolderCommandsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultDocumentSuggestion>> GetTopicSuggestionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultTopicSuggestion>> GetTopicTargetSuggestionsAsync(
        VaultTopicSuggestionContext? context = null,
        CancellationToken cancellationToken = default,
        string? searchText = null,
        int? maxResults = null);

    Task<IReadOnlyList<VaultDynamicValueSuggestion>> GetDynamicValueSuggestionsAsync(
        string commandName,
        CancellationToken cancellationToken = default);
}

public sealed record VaultFolderCommand(string CommandName, string FolderName, string FolderPath);

public sealed record VaultDocumentSuggestion(string Title, string FilePath, string RelativePath);

public sealed record VaultTopicSuggestionContext(string CommandName, string Value);

public sealed record VaultTopicSuggestion(string Title, string FilePath, string RelativePath, VaultTopicSuggestionKind Kind)
{
    public bool IsFolder => Kind == VaultTopicSuggestionKind.Folder;
}

public enum VaultTopicSuggestionKind
{
    File,
    Folder
}

public sealed record VaultDynamicValueSuggestion(string Value, string FilePath, bool IsFolder);
