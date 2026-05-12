using Avalonia;
using Notey.App.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notey.App.Diagnostics;

namespace Notey.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (DiagnosticsCommand.TryParse(args, out var diagnosticsPath))
        {
            ExportDiagnosticsAsync(args, diagnosticsPath).GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp(string[] args)
    {
        var host = HostBootstrapper.Create(args);

        return AppBuilder
            .Configure(() => new App(host))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

    private static async Task ExportDiagnosticsAsync(string[] args, string? diagnosticsPath)
    {
        using var host = HostBootstrapper.Create(args);
        await host.StartAsync();
        try
        {
            await host.Services.GetRequiredService<DiagnosticsReportWriter>()
                .WriteAsync(diagnosticsPath);
        }
        finally
        {
            await host.StopAsync(TimeSpan.FromSeconds(5));
        }
    }
}
