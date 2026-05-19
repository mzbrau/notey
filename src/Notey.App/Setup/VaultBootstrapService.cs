using Notey.Vault.Abstractions;
using Notey.Vault.Linking;

namespace Notey.App.Setup;

public sealed record VaultBootstrapRequest(
    IReadOnlyList<string>? Customers = null,
    IReadOnlyList<string>? Projects = null,
    IReadOnlyList<string>? Topics = null);

public sealed class VaultBootstrapService(
    IVaultWorkspace workspace,
    IVaultEntityStore entityStore)
{
    private static readonly string[] FixedNoteHeadings = ["Customers", "Projects", "Topics"];

    public async Task BootstrapAsync(VaultBootstrapRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var paths = workspace.GetPaths();
        Directory.CreateDirectory(paths.RootPath);
        Directory.CreateDirectory(paths.ImagesPath);
        Directory.CreateDirectory(paths.NotesPath);
        Directory.CreateDirectory(paths.DraftPath);
        Directory.CreateDirectory(paths.PeoplePath);

        foreach (var heading in FixedNoteHeadings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(paths.NotesPath, heading));
        }

        await CreateDynamicFoldersAsync(paths, "Customers", request.Customers ?? [], cancellationToken);
        await CreateDynamicFoldersAsync(paths, "Projects", request.Projects ?? [], cancellationToken);
        await CreateDynamicFoldersAsync(paths, "Topics", request.Topics ?? [], cancellationToken);
        await EnsureEntitiesAsync(VaultEntityKind.Project, request.Projects ?? [], cancellationToken);
        await EnsureEntitiesAsync(VaultEntityKind.Topic, request.Topics ?? [], cancellationToken);
    }

    private static Task CreateDynamicFoldersAsync(
        VaultPaths paths,
        string heading,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(paths.NotesPath, heading);
        foreach (var value in NormalizeValues(values))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(root, ObsidianLinkBuilder.GetSafeFileStem(value)));
        }

        return Task.CompletedTask;
    }

    private async Task EnsureEntitiesAsync(
        VaultEntityKind kind,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        foreach (var value in NormalizeValues(values))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await entityStore.EnsureAsync(kind, value, cancellationToken);
        }
    }

    private static IReadOnlyList<string> NormalizeValues(IReadOnlyList<string> values)
    {
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => ObsidianLinkBuilder.NormalizeDisplayName(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
