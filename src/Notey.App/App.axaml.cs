using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notey.App.Configuration;
using Notey.App.ScreenshotEditor;
using Notey.App.Views;
using Notey.Core.Configuration;
using Notey.Core.Platform;

namespace Notey.App;

public sealed class App(IHost host) : Application
{
    private bool _platformIntegrationInitialized;

    public App()
        : this(HostBootstrapper.Create([]))
    {
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        host.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            var platformRuntime = host.Services.GetRequiredService<IPlatformRuntime>();
            mainWindow.HideInsteadOfClose = platformRuntime.IsWindows;
            desktop.MainWindow = mainWindow;
            mainWindow.Opened += async (_, _) => await InitializePlatformIntegrationAsync(desktop, mainWindow);
            mainWindow.SettingsSaved += async (_, _) => await RegisterGlobalHotkeysAsync(mainWindow);
            desktop.Exit += (_, _) =>
            {
                host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializePlatformIntegrationAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindow mainWindow)
    {
        if (_platformIntegrationInitialized)
        {
            return;
        }

        _platformIntegrationInitialized = true;

        var trayService = host.Services.GetRequiredService<ITrayService>();

        await trayService.InitializeAsync(new TrayServiceRegistration(
            _ => ActivateMainWindowAsync(mainWindow, createNewNote: false),
            _ => ActivateMainWindowAsync(mainWindow, createNewNote: true),
            _ => ExitAsync(mainWindow)));

        await RegisterGlobalHotkeysAsync(mainWindow);
    }

    private async Task RegisterGlobalHotkeysAsync(MainWindow mainWindow)
    {
        var globalHotkeyService = host.Services.GetRequiredService<IGlobalHotkeyService>();
        var options = host.Services.GetRequiredService<NoteyOptions>();
        var coordinator = host.Services.GetRequiredService<ScreenshotCaptureCoordinator>();

        try
        {
            await globalHotkeyService.UnregisterAllAsync();
            await globalHotkeyService.RegisterAsync(new GlobalHotkeyRegistration(
                "Open note",
                options.Hotkeys.OpenNote,
                _ => ActivateMainWindowAsync(mainWindow, createNewNote: false)));
            await globalHotkeyService.RegisterAsync(new GlobalHotkeyRegistration(
                "Full-screen screenshot to clipboard",
                options.Hotkeys.CaptureFullScreen,
                token => coordinator.HandleHotkeyAsync(ScreenshotHotkeyAction.FullScreenToClipboard, token)));
            await globalHotkeyService.RegisterAsync(new GlobalHotkeyRegistration(
                "Region screenshot to clipboard",
                options.Hotkeys.CaptureRegionClipboard,
                token => coordinator.HandleHotkeyAsync(ScreenshotHotkeyAction.RegionToClipboard, token)));
            await globalHotkeyService.RegisterAsync(new GlobalHotkeyRegistration(
                "Region screenshot to editor",
                options.Hotkeys.CaptureRegionEditor,
                token => coordinator.HandleHotkeyAsync(ScreenshotHotkeyAction.RegionToEditor, token)));
            await globalHotkeyService.RegisterAsync(new GlobalHotkeyRegistration(
                "Window screenshot to editor",
                options.Hotkeys.CaptureWindowEditor,
                token => coordinator.HandleHotkeyAsync(ScreenshotHotkeyAction.WindowToEditor, token)));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            host.Services.GetRequiredService<ILogger<App>>()
                .LogError(ex, "Unable to register global hotkeys.");
            mainWindow.ReportHotkeyRegistrationFailure();
        }
        catch (InvalidOperationException ex)
        {
            host.Services.GetRequiredService<ILogger<App>>()
                .LogError(ex, "Unable to register global hotkeys.");
            mainWindow.ReportHotkeyRegistrationFailure();
        }
        catch (ArgumentException ex)
        {
            host.Services.GetRequiredService<ILogger<App>>()
                .LogError(ex, "Configured global hotkey is invalid.");
            mainWindow.ReportHotkeyRegistrationFailure();
        }
        catch (FormatException ex)
        {
            host.Services.GetRequiredService<ILogger<App>>()
                .LogError(ex, "Configured global hotkey is invalid.");
            mainWindow.ReportHotkeyRegistrationFailure();
        }
    }

    private static async ValueTask ActivateMainWindowAsync(MainWindow mainWindow, bool createNewNote)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (mainWindow.IsCaptureInProgress)
            {
                return;
            }

            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }

            if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            }

            mainWindow.Activate();

            if (createNewNote)
            {
                await mainWindow.StartNewNoteAsync();
            }
            else
            {
                await mainWindow.ActivateOrResumeAsync();
            }
        });
    }

    private static async ValueTask ExitAsync(MainWindow mainWindow)
    {
        await Dispatcher.UIThread.InvokeAsync(mainWindow.RequestExit);
    }
}
