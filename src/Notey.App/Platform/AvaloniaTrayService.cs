using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Notey.Core.Platform;

namespace Notey.App.Platform;

public sealed class AvaloniaTrayService(ILogger<AvaloniaTrayService> logger) : ITrayService, IDisposable
{
    private TrayIcon? _trayIcon;
    private bool _initialized;

    public ValueTask InitializeAsync(TrayServiceRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (_initialized)
        {
            return ValueTask.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var showItem = new NativeMenuItem("Show Notey");
        showItem.Click += async (_, _) => await InvokeTrayActionAsync("show", registration.ShowAsync);

        var newNoteItem = new NativeMenuItem("New note");
        newNoteItem.Click += async (_, _) => await InvokeTrayActionAsync("new note", registration.NewNoteAsync);

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += async (_, _) => await InvokeTrayActionAsync("exit", registration.ExitAsync);

        var menu = new NativeMenu
        {
            Items =
            {
                showItem,
                newNoteItem,
                new NativeMenuItemSeparator(),
                exitItem
            }
        };

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Notey",
            Icon = LoadIcon(),
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += async (_, _) => await InvokeTrayActionAsync("show", registration.ShowAsync);
        _initialized = true;
        logger.LogInformation("Tray integration initialized.");

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private async Task InvokeTrayActionAsync(string actionName, Func<CancellationToken, ValueTask> action)
    {
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tray action {ActionName} failed.", actionName);
        }
    }

    private static WindowIcon? LoadIcon()
    {
        return LoadIcon("avares://Notey/Assets/notey.ico")
            ?? LoadIcon("avares://Notey/Assets/notey.png");
    }

    private static WindowIcon? LoadIcon(string assetUri)
    {
        var uri = new Uri(assetUri);
        return AssetLoader.Exists(uri)
            ? new WindowIcon(AssetLoader.Open(uri))
            : null;
    }
}
