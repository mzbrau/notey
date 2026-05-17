using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notey.AI.Abstractions;
using Notey.AI.Providers;
using Notey.App.Diagnostics;
using Notey.App.Processing;
using Notey.App.Platform;
using Notey.App.Views;
using Notey.Capture.Abstractions;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Core.Platform;
using Notey.Ocr;
using Notey.Vault.Abstractions;
using Notey.Vault.Documents;
using Notey.Vault.Linking;
using Notey.Vault.Notes;
using Notey.Vault.Tasks;

namespace Notey.App.Configuration;

public static class HostBootstrapper
{
    public static IHost Create(string[] args)
    {
        return Host
            .CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            })
            .ConfigureServices((context, services) =>
            {
                var options = new NoteyOptions();
                var platformRuntime = new PlatformRuntime();
                context.Configuration.GetSection(NoteyOptions.SectionName).Bind(options);
                WarnIfLegacyVaultPathKeysConfigured(context.Configuration);

                services.AddSingleton(options);
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<NoteTemplateFactory>();
                services.AddSingleton<NoteFileNameGenerator>();
                services.AddSingleton<DiagnosticsReportWriter>();
                services.AddSingleton<NoteySettingsStore>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<IPlatformRuntime>(platformRuntime);
                services.AddSingleton<IScreenshotAnalysisService, UnconfiguredScreenshotAnalysisService>();
                services.AddHttpClient();
                services.AddSingleton<IAiProviderRegistry>(serviceProvider =>
                    new AiProviderRegistry(
                        OpenAiCompatibleAiProviderFactory.CreateProviders(
                            options.Ai,
                            () => serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Notey.OpenAiCompatible")),
                        string.IsNullOrWhiteSpace(options.Ai.DefaultProviderId) ? "default" : options.Ai.DefaultProviderId));
                services.AddSingleton<ITesseractOcrEngine, TesseractCliOcrEngine>();
                services.AddSingleton<IVaultWorkspace, FileSystemVaultWorkspace>();
                services.AddSingleton<IDocumentStoreIndex, FileSystemDocumentStoreIndex>();
                services.AddSingleton<ObsidianLinkBuilder>();
                services.AddSingleton<IVaultEntityStore, FileSystemVaultEntityStore>();
                services.AddSingleton<INoteDraftStore, FileSystemNoteDraftStore>();
                services.AddSingleton<ITaskStore, FileSystemTaskStore>();
                services.AddSingleton<DraftProcessingService>();
                services.AddSingleton<IRecentNoteChooser, RecentNoteDialogChooser>();

                if (platformRuntime.IsWindows)
                {
                    services.AddSingleton<IScreenSnipService, WindowsScreenSnipService>();
                    services.AddSingleton<IGlobalHotkeyService, WindowsGlobalHotkeyService>();
                    services.AddSingleton<ITrayService, AvaloniaTrayService>();
                }
                else
                {
                    services.AddSingleton<IScreenSnipService, UnavailableScreenSnipService>();
                    services.AddSingleton<IGlobalHotkeyService, NoOpGlobalHotkeyService>();
                    services.AddSingleton<ITrayService, NoOpTrayService>();
                }
            })
            .Build();
    }

    private static void WarnIfLegacyVaultPathKeysConfigured(IConfiguration configuration)
    {
        var vaultSection = configuration.GetSection($"{NoteyOptions.SectionName}:Vault");
        var legacyKeys = vaultSection
            .GetChildren()
            .Select(static child => child.Key)
            .Where(static key =>
                key is "NotesPath" or "PeoplePath" or "TopicsPath" or "ProjectsPath" or "ScreenshotPath")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (legacyKeys.Length == 0)
        {
            return;
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
        });
        var logger = loggerFactory.CreateLogger("Notey.Configuration");
        logger.LogWarning(
            "Notey no longer supports Notey:Vault legacy path keys ({Keys}). Use Notey:Vault:RootPath and Notey-owned folders under that root instead.",
            string.Join(", ", legacyKeys));
    }
}
