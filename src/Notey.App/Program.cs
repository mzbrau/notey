using Avalonia;
using Notey.App.Configuration;

namespace Notey.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
}
