using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Notey.Core.Configuration;
using Notey.Core.Platform;

namespace Notey.App.Diagnostics;

public sealed class DiagnosticsReportWriter(
    NoteyOptions options,
    IPlatformRuntime platformRuntime,
    TimeProvider timeProvider,
    ILogger<DiagnosticsReportWriter> logger)
{
    public async Task<string> WriteAsync(string? outputPath = null, CancellationToken cancellationToken = default)
    {
        var generatedAt = timeProvider.GetUtcNow();
        var path = ResolveOutputPath(outputPath, generatedAt);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = new List<string>
        {
            "# Notey diagnostics",
            string.Empty,
            $"Generated: {generatedAt:O}",
            $"App version: {GetAppVersion()}",
            $"OS: {RuntimeInformation.OSDescription}",
            $"Architecture: {RuntimeInformation.OSArchitecture}",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"Windows runtime: {platformRuntime.IsWindows}",
            string.Empty,
            "## Vault",
            string.Empty,
            $"- Root path configured: {FormatConfigured(options.Vault.RootPath)}",
            "- Owned paths: Images, Notes, Notes/Draft, People",
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
            $"- Tesseract data path configured: {IsConfigured(options.Ocr.TesseractDataPath)}",
            $"- Default language: {FormatConfigured(options.Ocr.DefaultLanguage)}",
        ]);

        await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", cancellationToken);
        logger.LogInformation("Diagnostics report exported to {Path}.", path);
        return path;
    }

    private static string ResolveOutputPath(string? outputPath, DateTimeOffset generatedAt)
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
            $"notey-diagnostics-{generatedAt:yyyyMMdd-HHmmss}.md");
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
