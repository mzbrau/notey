using Avalonia.Controls;
using Notey.App.Configuration;
using Notey.App.Imports;
using Notey.Core.Configuration;
using Notey.Vault.Abstractions;

namespace Notey.App.Setup;

public interface ISetupWorkflow
{
    Task<SetupWorkflowResult> RunInitialSetupAsync(Window owner, CancellationToken cancellationToken = default);

    Task<SetupWorkflowResult> RunImportAsync(Window owner, CancellationToken cancellationToken = default);
}

public sealed class SetupWorkflow(
    NoteyOptions options,
    NoteySettingsStore settingsStore,
    VaultBootstrapService vaultBootstrapService,
    ExistingDocumentImportService importService) : ISetupWorkflow
{
    public async Task<SetupWorkflowResult> RunInitialSetupAsync(Window owner, CancellationToken cancellationToken = default)
    {
        var result = await SetupWizardWindow.ShowAsync(owner, options, SetupWizardMode.InitialSetup);
        if (result is null)
        {
            return SetupWorkflowResult.Cancelled("Setup cancelled.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var updatedOptions = NoteySettingsStore.Clone(options);
        updatedOptions.Vault.RootPath = result.VaultRootPath.Trim();
        await BootstrapBeforeSavingAsync(updatedOptions, result.ToBootstrapRequest(), cancellationToken);
        await settingsStore.SaveAsync(updatedOptions, cancellationToken);

        var importResult = string.IsNullOrWhiteSpace(result.SourceFolderPath)
            ? null
            : await importService.ImportFolderAsync(result.SourceFolderPath, cancellationToken: cancellationToken);
        return SetupWorkflowResult.Success(BuildMessage("Setup complete.", importResult));
    }

    public async Task<SetupWorkflowResult> RunImportAsync(Window owner, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Vault.RootPath))
        {
            return SetupWorkflowResult.Cancelled("Setup required before import.");
        }

        var result = await SetupWizardWindow.ShowAsync(owner, options, SetupWizardMode.ImportOnly);
        if (result is null || string.IsNullOrWhiteSpace(result.SourceFolderPath))
        {
            return SetupWorkflowResult.Cancelled("Import cancelled.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await vaultBootstrapService.BootstrapAsync(result.ToBootstrapRequest(), cancellationToken);
        var importResult = await importService.ImportFolderAsync(result.SourceFolderPath, cancellationToken: cancellationToken);
        return SetupWorkflowResult.Success(BuildMessage("Import complete.", importResult));
    }

    private static string BuildMessage(string prefix, ExistingDocumentImportResult? importResult)
    {
        if (importResult is null)
        {
            return prefix;
        }

        return $"{prefix} Imported {importResult.ImportedCount}, skipped {importResult.SkippedCount}, failed {importResult.FailedCount}.";
    }

    private static async Task BootstrapBeforeSavingAsync(
        NoteyOptions updatedOptions,
        VaultBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        var workspace = new FileSystemVaultWorkspace(updatedOptions);
        var bootstrap = new VaultBootstrapService(workspace);
        await bootstrap.BootstrapAsync(request, cancellationToken);
    }
}

public sealed record SetupWorkflowResult(bool Completed, string Message)
{
    public static SetupWorkflowResult Success(string message)
    {
        return new SetupWorkflowResult(true, message);
    }

    public static SetupWorkflowResult Cancelled(string message)
    {
        return new SetupWorkflowResult(false, message);
    }
}
