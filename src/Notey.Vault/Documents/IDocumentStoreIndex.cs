namespace Notey.Vault.Documents;

public interface IDocumentStoreIndex
{
    Task<IReadOnlyList<VaultFolderCommand>> GetFolderCommandsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultDocumentSuggestion>> GetTopicSuggestionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultDynamicValueSuggestion>> GetDynamicValueSuggestionsAsync(
        string commandName,
        CancellationToken cancellationToken = default);
}

public sealed record VaultFolderCommand(string CommandName, string FolderName, string FolderPath);

public sealed record VaultDocumentSuggestion(string Title, string FilePath, string RelativePath);

public sealed record VaultDynamicValueSuggestion(string Value, string FilePath, bool IsFolder);
