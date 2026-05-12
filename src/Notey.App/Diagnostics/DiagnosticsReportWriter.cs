using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Notey.Core.Configuration;
using Notey.Core.Platform;
using Notey.Pipelines.Catalog;

namespace Notey.App.Diagnostics;

public sealed class DiagnosticsReportWriter(
    NoteyOptions options,
    PipelineCatalog pipelineCatalog,
    IPlatformRuntime platformRuntime,
    TimeProvider timeProvider,
    ILogger<DiagnosticsReportWriter> logger)
{
    public async Task<string> WriteAsync(string? outputPath = null, CancellationToken cancellationToken = default)
    {
        var path = ResolveOutputPath(outputPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = new List<string>
        {
            "# Notey diagnostics",
            string.Empty,
            $"Generated: {timeProvider.GetUtcNow():O}",
            $"App version: {GetAppVersion()}",
            $"OS: {RuntimeInformation.OSDescription}",
            $"Architecture: {RuntimeInformation.OSArchitecture}",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"Windows runtime: {platformRuntime.IsWindows}",
            string.Empty,
            "## Vault",
            string.Empty,
            $"- Root path configured: {FormatConfigured(options.Vault.RootPath)}",
            $"- Notes path: {options.Vault.NotesPath}",
            $"- People path: {options.Vault.PeoplePath}",
            $"- Topics path: {options.Vault.TopicsPath}",
            $"- Projects path: {options.Vault.ProjectsPath}",
            $"- Screenshot path: {options.Vault.ScreenshotPath}",
            string.Empty,
            "## AI providers",
            string.Empty,
            $"- Default provider id: {FormatConfigured(options.Ai.DefaultProviderId)}",
            $"- Default base URL configured: {IsConfigured(options.Ai.BaseUrl)}",
            $"- Default model configured: {IsConfigured(options.Ai.ModelName)}",
            $"- Default API key configured: {IsApiKeyConfigured(options.Ai.ApiKey, options.Ai.ApiKeyEnvironmentVariable)}",
            $"- API key environment variable: {FormatConfigured(options.Ai.ApiKeyEnvironmentVariable)}",
            $"- Plaintext API key storage enabled: {options.Ai.StoreApiKeyInPlaintext}",
        };

        foreach (var provider in options.Ai.Providers)
        {
            var providerApiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? options.Ai.ApiKey : provider.ApiKey;
            var providerApiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(provider.ApiKeyEnvironmentVariable)
                ? options.Ai.ApiKeyEnvironmentVariable
                : provider.ApiKeyEnvironmentVariable;
            lines.Add($"- Provider `{provider.Id}`: type={provider.Type}, baseUrlConfigured={IsConfigured(provider.BaseUrl)}, modelConfigured={IsConfigured(provider.ModelName)}, apiKeyConfigured={IsApiKeyConfigured(providerApiKey, providerApiKeyEnvironmentVariable)}, env={FormatConfigured(providerApiKeyEnvironmentVariable)}");
        }

        lines.AddRange([
            string.Empty,
            "## OCR",
            string.Empty,
            $"- Tesseract executable: {FormatConfigured(options.Ocr.TesseractExecutablePath)}",
            $"- Tesseract data path configured: {IsConfigured(options.Ocr.TesseractDataPath)}",
            $"- Default language: {FormatConfigured(options.Ocr.DefaultLanguage)}",
            string.Empty,
            "## Pipelines",
            string.Empty,
            $"- Definition file: {FormatConfigured(options.Pipelines.DefinitionFilePath)}",
            $"- Default screenshot pipeline: {FormatConfigured(options.Pipelines.DefaultScreenshotPipelineId)}",
        ]);

        await AppendPipelineDiagnosticsAsync(lines, cancellationToken);
        await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", cancellationToken);
        logger.LogInformation("Diagnostics report exported to {Path}.", path);
        return path;
    }

    private async Task AppendPipelineDiagnosticsAsync(ICollection<string> lines, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await pipelineCatalog.LoadAsync(cancellationToken);
            if (snapshot.LoadWarnings.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("### Pipeline catalog warnings");
                foreach (var warning in snapshot.LoadWarnings)
                {
                    lines.Add($"- {warning}");
                }
            }

            lines.Add(string.Empty);
            lines.Add("### Pipeline validation");
            foreach (var entry in snapshot.Entries)
            {
                var state = entry.ValidationResult.IsValid ? "valid" : "invalid";
                lines.Add($"- `{entry.Definition.Id}`: {state}, enabled={entry.Definition.Enabled}, output={entry.Definition.FinalOutputType}");
                foreach (var error in entry.ValidationResult.Errors)
                {
                    lines.Add($"  - Error: {error}");
                }

                foreach (var warning in entry.ValidationResult.Warnings)
                {
                    lines.Add($"  - Warning: {warning}");
                }
            }
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to load pipeline definitions while exporting diagnostics.");
            lines.Add($"- Pipeline diagnostics unavailable: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Notey does not have permission to read pipeline definitions while exporting diagnostics.");
            lines.Add($"- Pipeline diagnostics unavailable: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Pipeline configuration prevented diagnostics export.");
            lines.Add($"- Pipeline diagnostics unavailable: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Invalid pipeline configuration prevented diagnostics export.");
            lines.Add($"- Pipeline diagnostics unavailable: {ex.Message}");
        }
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(
            baseDirectory,
            "Notey",
            "Diagnostics",
            $"notey-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.md");
    }

    private static string GetAppVersion()
    {
        return typeof(DiagnosticsReportWriter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
    }

    private static string IsConfigured(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "no" : "yes";
    }

    private static string IsApiKeyConfigured(string? configuredApiKey, string? environmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return "yes";
        }

        var variableName = string.IsNullOrWhiteSpace(environmentVariable)
            ? "NOTEY_AI_API_KEY"
            : environmentVariable.Trim();

        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName)) ? "no" : "yes";
    }

    private static string FormatConfigured(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<not configured>" : value.Trim();
    }
}
