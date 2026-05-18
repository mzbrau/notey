using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notey.AI.Abstractions;
using Notey.AI.Providers;
using Notey.App.Assistant;
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
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace Notey.App.Configuration;

public static class HostBootstrapper
{
    private const string LogOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static IHost Create(string[] args)
    {
        return Host
            .CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
            })
            .UseSerilog((context, _, loggerConfiguration) =>
            {
                var logFilePath = ResolveLogFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

                loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithThreadId()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .WriteTo.Console(outputTemplate: LogOutputTemplate)
                    .WriteTo.File(
                        path: logFilePath,
                        outputTemplate: LogOutputTemplate,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 31,
                        fileSizeLimitBytes: 100 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: false);

                var otlpEndpoint = context.Configuration["Logging:OpenTelemetry:Endpoint"];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    loggerConfiguration.WriteTo.OpenTelemetry(options =>
                    {
                        options.Endpoint = otlpEndpoint;
                        options.Protocol = OtlpProtocol.Grpc;
                        options.ResourceAttributes = new Dictionary<string, object>
                        {
                            ["service.name"] = "Notey",
                        };
                    });
                }
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
                            () => serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Notey.OpenAiCompatible"),
                            serviceProvider.GetRequiredService<ILoggerFactory>()),
                        string.IsNullOrWhiteSpace(options.Ai.DefaultProviderId) ? "default" : options.Ai.DefaultProviderId));
                services.AddSingleton<ITesseractOcrEngine, TesseractNativeOcrEngine>();
                services.AddSingleton<IVaultWorkspace, FileSystemVaultWorkspace>();
                services.AddSingleton<IDocumentStoreIndex, FileSystemDocumentStoreIndex>();
                services.AddSingleton<ObsidianLinkBuilder>();
                services.AddSingleton<IVaultEntityStore, FileSystemVaultEntityStore>();
                services.AddSingleton<INoteDraftStore, FileSystemNoteDraftStore>();
                services.AddSingleton<ITaskStore, FileSystemTaskStore>();
                services.AddSingleton<DraftProcessingService>();
                services.AddSingleton<NoteyAssistantService>();
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

    private static string ResolveLogFilePath()
    {
        var logsFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(
                ResolveWritableBasePath(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                "Notey", "Logs")
            : ResolveNonWindowsLogFolder();

        return Path.Combine(logsFolder, "notey.log");
    }

    private static string ResolveNonWindowsLogFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Path.IsPathFullyQualified(userProfile))
        {
            return Path.Combine(userProfile, ".local", "share", "Notey", "Logs");
        }

        return Path.Combine(
            ResolveWritableBasePath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            "Notey",
            "Logs");
    }

    private static string ResolveWritableBasePath(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Path.IsPathFullyQualified(candidate))
            {
                return candidate;
            }
        }

        if (Path.IsPathFullyQualified(AppContext.BaseDirectory))
        {
            return AppContext.BaseDirectory;
        }

        return Path.GetTempPath();
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

        Log.Warning(
            "Notey no longer supports Notey:Vault legacy path keys ({Keys}). Use Notey:Vault:RootPath and Notey-owned folders under that root instead.",
            string.Join(", ", legacyKeys));
    }
}
