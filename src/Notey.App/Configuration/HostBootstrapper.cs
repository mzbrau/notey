using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notey.AI.Abstractions;
using Notey.App.Platform;
using Notey.App.Views;
using Notey.Capture.Abstractions;
using Notey.Core.Configuration;
using Notey.Core.Notes;
using Notey.Core.Platform;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;
using Notey.Vault.Notes;

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

                services.AddSingleton(options);
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<NoteTemplateFactory>();
                services.AddSingleton<NoteFileNameGenerator>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<IPlatformRuntime>(platformRuntime);
                services.AddSingleton<IScreenSnipService, UnavailableScreenSnipService>();
                services.AddSingleton<IScreenshotAnalysisService, UnconfiguredScreenshotAnalysisService>();
                services.AddSingleton<IVaultWorkspace, FileSystemVaultWorkspace>();
                services.AddSingleton<ObsidianLinkBuilder>();
                services.AddSingleton<IVaultEntityStore, FileSystemVaultEntityStore>();
                services.AddSingleton<INoteDraftStore, FileSystemNoteDraftStore>();

                if (platformRuntime.IsWindows)
                {
                    services.AddSingleton<IGlobalHotkeyService, WindowsGlobalHotkeyService>();
                    services.AddSingleton<ITrayService, AvaloniaTrayService>();
                }
                else
                {
                    services.AddSingleton<IGlobalHotkeyService, NoOpGlobalHotkeyService>();
                    services.AddSingleton<ITrayService, NoOpTrayService>();
                }
            })
            .Build();
    }
}
