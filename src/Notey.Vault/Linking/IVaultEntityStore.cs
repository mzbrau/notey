namespace Notey.Vault.Linking;

public interface IVaultEntityStore
{
    Task<IReadOnlyList<VaultEntity>> GetAllAsync(VaultEntityKind kind, CancellationToken cancellationToken = default);

    Task<VaultEntity> EnsureAsync(VaultEntityKind kind, string name, CancellationToken cancellationToken = default);
}
