using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notey.AI.Abstractions;
using Notey.App.Platform;
using Notey.App.Views;
using Notey.Capture.Abstractions;
using Notey.Core.Configuration;
using Notey.Core.Platform;
using Notey.Vault.Abstractions;

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
                context.Configuration.GetSection(NoteyOptions.SectionName).Bind(options);

                services.AddSingleton(options);
                services.AddSingleton<MainWindow>();
                services.AddSingleton<IPlatformRuntime, PlatformRuntime>();
                services.AddSingleton<IGlobalHotkeyService, NoOpGlobalHotkeyService>();
                services.AddSingleton<ITrayService, NoOpTrayService>();
                services.AddSingleton<IScreenSnipService, UnavailableScreenSnipService>();
                services.AddSingleton<IScreenshotAnalysisService, UnconfiguredScreenshotAnalysisService>();
                services.AddSingleton<IVaultWorkspace, FileSystemVaultWorkspace>();
            })
            .Build();
    }
}
