using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.AI.Providers;
using Notey.Core.Configuration;
using Notey.Core.Platform;

namespace Notey.App.Configuration;

public sealed record NoteySettingsSaveResult(bool RestartRequired, string Message);

public sealed class NoteySettingsStore(
    NoteyOptions currentOptions,
    IAiProviderRegistry aiProviderRegistry,
    IHttpClientFactory httpClientFactory,
    ILogger<NoteySettingsStore> logger,
    string? localSettingsPath = null,
    ILoggerFactory? loggerFactory = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private readonly ILoggerFactory aiProviderLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

    public string LocalSettingsPath { get; } = localSettingsPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");

    public static NoteyOptions Clone(NoteyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new NoteyOptions
        {
            Ui = new UiOptions
            {
                Theme = options.Ui.Theme,
                DefaultWindowWidth = options.Ui.DefaultWindowWidth,
                DefaultWindowHeight = options.Ui.DefaultWindowHeight
            },
            Hotkeys = new HotkeyOptions
            {
                OpenNote = options.Hotkeys.OpenNote
            },
            Vault = new VaultOptions
            {
                RootPath = options.Vault.RootPath
            },
            Ai = new AiOptions
            {
                DefaultProviderId = options.Ai.DefaultProviderId,
                BaseUrl = options.Ai.BaseUrl,
                ApiKey = options.Ai.ApiKey,
                ApiKeyEnvironmentVariable = options.Ai.ApiKeyEnvironmentVariable,
                ModelName = options.Ai.ModelName,
                RequestTimeoutSeconds = options.Ai.RequestTimeoutSeconds,
                StoreApiKeyInPlaintext = options.Ai.StoreApiKeyInPlaintext,
                Providers = options.Ai.Providers.Select(static provider => new AiProviderOptions
                {
                    Id = provider.Id,
                    Type = provider.Type,
                    BaseUrl = provider.BaseUrl,
                    ApiKey = provider.ApiKey,
                    ApiKeyEnvironmentVariable = provider.ApiKeyEnvironmentVariable,
                    ModelName = provider.ModelName,
                    RequestTimeoutSeconds = provider.RequestTimeoutSeconds
                }).ToList()
            },
            Ocr = new OcrOptions
            {
                TesseractDataPath = options.Ocr.TesseractDataPath,
                DefaultLanguage = options.Ocr.DefaultLanguage
            },
            Spellcheck = new SpellcheckOptions
            {
                Enabled = options.Spellcheck.Enabled,
                Language = options.Spellcheck.Language
            }
        };
    }

    public static IReadOnlyList<string> Validate(NoteyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        if (options.Ui.DefaultWindowWidth is < 640 or > 3840)
        {
            errors.Add("Default window width must be between 640 and 3840.");
        }

        if (options.Ui.DefaultWindowHeight is < 360 or > 2160)
        {
            errors.Add("Default window height must be between 360 and 2160.");
        }

        ValidateRequired(errors, options.Hotkeys.OpenNote, "Open-note hotkey is required.");
        try
        {
            HotkeyGesture.Parse(options.Hotkeys.OpenNote);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            errors.Add($"Open-note hotkey is invalid: {ex.Message}");
        }

        ValidateRequired(errors, options.Ai.DefaultProviderId, "Default AI provider id is required.");
        ValidateRequired(errors, options.Ai.ApiKeyEnvironmentVariable, "AI API-key environment variable is required.");
        if (!string.IsNullOrWhiteSpace(options.Ai.BaseUrl)
            && !Uri.TryCreate(options.Ai.BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add("AI base URL must be an absolute URL.");
        }

        if (options.Ai.RequestTimeoutSeconds <= 0)
        {
            errors.Add("AI request timeout must be greater than zero.");
        }

        if (!options.Ai.StoreApiKeyInPlaintext && !string.IsNullOrWhiteSpace(options.Ai.ApiKey))
        {
            errors.Add("Enable plaintext API-key storage before saving an API key value.");
        }

        ValidateRequired(errors, options.Ocr.DefaultLanguage, "OCR default language is required.");
        ValidateRequired(errors, options.Spellcheck.Language, "Spellcheck language is required.");
        if (options.Spellcheck.Enabled
            && !string.Equals(options.Spellcheck.Language, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Spellcheck language must be en-US.");
        }

        return errors;
    }

    public async Task<NoteySettingsSaveResult> SaveAsync(NoteyOptions updatedOptions, CancellationToken cancellationToken = default)
    {
        var errors = Validate(updatedOptions);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        var optionsToWrite = Clone(updatedOptions);
        if (!optionsToWrite.Ai.StoreApiKeyInPlaintext)
        {
            optionsToWrite.Ai.ApiKey = string.Empty;
            foreach (var provider in optionsToWrite.Ai.Providers)
            {
                provider.ApiKey = string.Empty;
            }
        }

        var restartRequired = RequiresRestart(currentOptions, updatedOptions);
        await WriteLocalSettingsAsync(optionsToWrite, cancellationToken);
        CopyInto(currentOptions, updatedOptions);
        RefreshAiProviders();

        var message = restartRequired
            ? "Settings saved. OCR changes apply after restart."
            : "Settings saved.";
        return new NoteySettingsSaveResult(restartRequired, message);
    }

    private async Task WriteLocalSettingsAsync(NoteyOptions options, CancellationToken cancellationToken)
    {
        var targetPath = Path.GetFullPath(LocalSettingsPath);
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            directory ?? AppContext.BaseDirectory,
            $"appsettings.Local.{Guid.NewGuid():N}.tmp");
        try
        {
            var document = new AppSettingsDocument { Notey = options };
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(document, SerializerOptions) + Environment.NewLine,
                cancellationToken);
            RestrictLocalSettingsPermissions(tempPath);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void RefreshAiProviders()
    {
        aiProviderRegistry.ReplaceProviders(
            OpenAiCompatibleAiProviderFactory.CreateProviders(
                currentOptions.Ai,
                () => httpClientFactory.CreateClient("Notey.OpenAiCompatible"),
                aiProviderLoggerFactory),
            string.IsNullOrWhiteSpace(currentOptions.Ai.DefaultProviderId) ? "default" : currentOptions.Ai.DefaultProviderId);
    }

    private void RestrictLocalSettingsPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "Unable to restrict permissions on local settings file {Path}.", path);
        }
    }

    private static void CopyInto(NoteyOptions target, NoteyOptions source)
    {
        var cloned = Clone(source);
        target.Ui = cloned.Ui;
        target.Hotkeys = cloned.Hotkeys;
        target.Vault = cloned.Vault;
        target.Ai = cloned.Ai;
        target.Ocr = cloned.Ocr;
        target.Spellcheck = cloned.Spellcheck;
    }

    private static bool RequiresRestart(NoteyOptions current, NoteyOptions updated)
    {
        return !string.Equals(current.Ocr.TesseractDataPath, updated.Ocr.TesseractDataPath, StringComparison.Ordinal)
            || !string.Equals(current.Ocr.DefaultLanguage, updated.Ocr.DefaultLanguage, StringComparison.Ordinal);
    }

    private static void ValidateRequired(ICollection<string> errors, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(message);
        }
    }

    private sealed class AppSettingsDocument
    {
        public NoteyOptions Notey { get; set; } = new();
    }
}
