using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Notey.App.Views;
using Notey.Capture.Abstractions;
using Notey.Core.Platform;

namespace Notey.App.ScreenshotEditor;

public enum ScreenshotHotkeyAction
{
    FullScreenToClipboard,
    RegionToClipboard,
    RegionToEditor,
    WindowToEditor
}

public sealed class ScreenshotCaptureCoordinator(
    MainWindowAccessor mainWindowAccessor,
    IScreenCaptureService screenCaptureService,
    IImageClipboardService imageClipboardService,
    INoteImageInserter noteImageInserter,
    ILogger<ScreenshotCaptureCoordinator> logger)
{
    private int _captureGate;

    public async ValueTask HandleHotkeyAsync(ScreenshotHotkeyAction action, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _captureGate, 1, 0) != 0)
        {
            logger.LogInformation("Ignoring screenshot hotkey because a capture is already in progress.");
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var mainWindow = mainWindowAccessor.GetMainWindow();
                if (mainWindow.IsCaptureInProgress)
                {
                    return;
                }

                mainWindow.BeginExternalCapture();
                var shouldRestore = mainWindow.IsVisible;
                try
                {
                    if (shouldRestore)
                    {
                        mainWindow.Hide();
                        await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
                    }

                    var capture = action switch
                    {
                        ScreenshotHotkeyAction.FullScreenToClipboard => await screenCaptureService.CaptureFullScreenAsync(cancellationToken),
                        ScreenshotHotkeyAction.RegionToClipboard or ScreenshotHotkeyAction.RegionToEditor
                            => await screenCaptureService.CaptureRegionAsync(cancellationToken),
                        ScreenshotHotkeyAction.WindowToEditor => await screenCaptureService.CaptureWindowAsync(cancellationToken),
                        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
                    };

                    if (action is ScreenshotHotkeyAction.FullScreenToClipboard or ScreenshotHotkeyAction.RegionToClipboard)
                    {
                        await imageClipboardService.CopyPngAsync(capture.PngBytes, cancellationToken);
                        logger.LogInformation("Screenshot copied to clipboard ({Width}x{Height}).", capture.Width, capture.Height);
                        return;
                    }

                    ScreenshotEditorWindow.ShowNew(
                        capture.PngBytes,
                        capture.Width,
                        capture.Height,
                        imageClipboardService,
                        noteImageInserter);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogDebug("Screenshot capture was cancelled by the user.");
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or Win32Exception or ExternalException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    logger.LogError(ex, "Screenshot hotkey action {Action} failed.", action);
                    mainWindow.ReportScreenshotCaptureFailure(ex.Message);
                }
                finally
                {
                    var clipboardOnly = action is ScreenshotHotkeyAction.FullScreenToClipboard
                        or ScreenshotHotkeyAction.RegionToClipboard;
                    mainWindow.EndExternalCapture(shouldRestore, activate: clipboardOnly);
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref _captureGate, 0);
        }
    }
}

/// <summary>
/// Lazily resolves MainWindow to avoid a circular DI dependency with the hotkey service.
/// </summary>
public sealed class MainWindowAccessor(IServiceProvider services)
{
    public MainWindow GetMainWindow()
        => services.GetRequiredService<MainWindow>();
}
