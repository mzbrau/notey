using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Notey.AI.Providers;
using Notey.App.Configuration;
using Notey.Core.Configuration;

namespace Notey.Tests;

public sealed class NoteySettingsStoreTests
{
    [Fact]
    public void Validate_rejects_invalid_hotkey_and_unapproved_plaintext_key()
    {
        var options = new NoteyOptions
        {
            Hotkeys = new HotkeyOptions { OpenNote = "not a hotkey" },
            Ai = new AiOptions
            {
                ApiKey = "test-secret",
                StoreApiKeyInPlaintext = false
            }
        };

        var errors = NoteySettingsStore.Validate(options);

        Assert.Contains(errors, static error => error.Contains("Open-note hotkey is invalid", StringComparison.Ordinal));
        Assert.Contains(errors, static error => error.Contains("Enable plaintext API-key storage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Save_writes_local_settings_atomically_and_mutates_current_options()
    {
        var root = CreateTempDirectory();
        try
        {
            var current = new NoteyOptions();
            var updated = NoteySettingsStore.Clone(current);
            updated.Hotkeys.OpenNote = "Ctrl+Shift+N";
            updated.Ui.DefaultWindowWidth = 1000;
            updated.Spellcheck.Enabled = false;
            var localSettingsPath = Path.Combine(root, "appsettings.Local.json");
            var store = CreateStore(current, localSettingsPath);

            var result = await store.SaveAsync(updated);

            var json = await File.ReadAllTextAsync(localSettingsPath);
            Assert.False(result.RestartRequired);
            Assert.Equal("Ctrl+Shift+N", current.Hotkeys.OpenNote);
            Assert.Equal(1000, current.Ui.DefaultWindowWidth);
            Assert.False(current.Spellcheck.Enabled);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(
                "Ctrl+Shift+N",
                document.RootElement.GetProperty("Notey").GetProperty("Hotkeys").GetProperty("OpenNote").GetString());
            Assert.False(document.RootElement.GetProperty("Notey").GetProperty("Spellcheck").GetProperty("Enabled").GetBoolean());
            Assert.DoesNotContain(Directory.GetFiles(root), static path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validate_rejects_unsupported_spellcheck_language()
    {
        var options = new NoteyOptions
        {
            Spellcheck = new SpellcheckOptions { Enabled = true, Language = "en-GB" }
        };

        var errors = NoteySettingsStore.Validate(options);

        Assert.Contains(errors, static error => error.Contains("Spellcheck language must be en-US", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Save_only_writes_api_key_when_plaintext_storage_is_enabled()
    {
        var root = CreateTempDirectory();
        try
        {
            var current = new NoteyOptions();
            var updated = NoteySettingsStore.Clone(current);
            updated.Ai.StoreApiKeyInPlaintext = true;
            updated.Ai.ApiKey = "test-secret";
            var localSettingsPath = Path.Combine(root, "appsettings.Local.json");
            var store = CreateStore(current, localSettingsPath);

            await store.SaveAsync(updated);

            var json = await File.ReadAllTextAsync(localSettingsPath);
            Assert.Contains("test-secret", json, StringComparison.Ordinal);
            Assert.Equal("test-secret", current.Ai.ApiKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Save_refreshes_ai_providers_with_injected_logger_factory()
    {
        var root = CreateTempDirectory();
        try
        {
            var current = new NoteyOptions();
            var updated = NoteySettingsStore.Clone(current);
            var localSettingsPath = Path.Combine(root, "appsettings.Local.json");
            var loggerFactory = new RecordingLoggerFactory();
            var store = CreateStore(current, localSettingsPath, loggerFactory);

            await store.SaveAsync(updated);

            Assert.Contains(
                loggerFactory.CreatedCategories,
                static category => string.Equals(category, typeof(OpenAiCompatibleAiProvider).FullName, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static NoteySettingsStore CreateStore(
        NoteyOptions options,
        string localSettingsPath,
        ILoggerFactory? loggerFactory = null)
    {
        return new NoteySettingsStore(
            options,
            new AiProviderRegistry(
                OpenAiCompatibleAiProviderFactory.CreateProviders(options.Ai, static () => new HttpClient(), NullLoggerFactory.Instance),
                "default"),
            new FakeHttpClientFactory(),
            NullLogger<NoteySettingsStore>.Instance,
            localSettingsPath,
            loggerFactory);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notey-settings-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly List<string> createdCategories = [];

        public IReadOnlyList<string> CreatedCategories => createdCategories;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            createdCategories.Add(categoryName);
            return NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
