using Microsoft.Extensions.Logging;
using Notey.App.Views;
using Notey.Capture.Abstractions;
using Notey.Vault.Abstractions;

namespace Notey.App.Platform;

public sealed class WindowsScreenSnipService(
    IVaultWorkspace workspace,
    TimeProvider timeProvider,
    ILogger<WindowsScreenSnipService> logger) : IScreenSnipService, IScreenCaptureService
{
    public async ValueTask<ScreenSnipResult> CaptureAsync(ScreenSnipMode mode, CancellationToken cancellationToken = default)
    {
        var capture = await CaptureRegionAsync(cancellationToken);
        var filePath = GetUniqueSnipPath(capture.CapturedAt);
        await File.WriteAllBytesAsync(filePath, capture.PngBytes, cancellationToken);
        logger.LogInformation(
            "Saved screen snip {FilePath} ({Width}x{Height}).",
            filePath,
            capture.Width,
            capture.Height);
        return new ScreenSnipResult(filePath, capture.CapturedAt, capture.Width, capture.Height, mode);
    }

    public async ValueTask<ScreenCaptureResult> CaptureFullScreenAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        var bounds = WindowsScreenCaptureHelper.GetMonitorBoundsUnderCursor();
        await Task.Delay(TimeSpan.FromMilliseconds(125), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var pngBytes = await Task.Run(() => WindowsScreenCaptureHelper.CaptureRegionToPngBytes(bounds), cancellationToken);
        var capturedAt = timeProvider.GetLocalNow();
        logger.LogInformation("Captured full-screen screenshot ({Width}x{Height}).", bounds.Width, bounds.Height);
        return new ScreenCaptureResult(pngBytes, capturedAt, bounds.Width, bounds.Height, ScreenCaptureKind.FullScreen, bounds.X, bounds.Y);
    }

    public async ValueTask<ScreenCaptureResult> CaptureRegionAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        var virtualBounds = WindowsScreenCaptureHelper.GetVirtualScreenBounds();
        var frozenPng = await Task.Run(() => WindowsScreenCaptureHelper.CaptureRegionToPngBytes(virtualBounds), cancellationToken);
        var selection = await ScreenSnipSelectionWindow.ShowSelectionAsync(frozenPng, virtualBounds, cancellationToken);
        if (selection is null)
        {
            throw new OperationCanceledException("Screen snip selection was cancelled.", cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var crop = WindowsScreenCaptureHelper.ClampSelection(virtualBounds, selection);
        var pngBytes = WindowsScreenCaptureHelper.CropPngBytes(frozenPng, virtualBounds, crop);
        var capturedAt = timeProvider.GetLocalNow();
        logger.LogInformation("Captured region screenshot ({Width}x{Height}).", crop.Width, crop.Height);
        return new ScreenCaptureResult(
            pngBytes,
            capturedAt,
            crop.Width,
            crop.Height,
            ScreenCaptureKind.Region,
            crop.X,
            crop.Y);
    }

    public async ValueTask<ScreenCaptureResult> CaptureWindowAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        var virtualBounds = WindowsScreenCaptureHelper.GetVirtualScreenBounds();
        var frozenPng = await Task.Run(() => WindowsScreenCaptureHelper.CaptureRegionToPngBytes(virtualBounds), cancellationToken);
        var selection = await WindowPickerWindow.ShowSelectionAsync(frozenPng, virtualBounds, cancellationToken);
        if (selection is null)
        {
            throw new OperationCanceledException("Window selection was cancelled.", cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var crop = WindowsScreenCaptureHelper.ClampSelection(virtualBounds, selection.Bounds);
        var pngBytes = WindowsScreenCaptureHelper.CropPngBytes(frozenPng, virtualBounds, crop);
        var capturedAt = timeProvider.GetLocalNow();
        logger.LogInformation("Captured window screenshot ({Width}x{Height}).", crop.Width, crop.Height);
        return new ScreenCaptureResult(
            pngBytes,
            capturedAt,
            crop.Width,
            crop.Height,
            ScreenCaptureKind.Window,
            crop.X,
            crop.Y);
    }

    private string GetUniqueSnipPath(DateTimeOffset capturedAt)
    {
        var screenshotPath = workspace.GetPaths().ImagesPath;
        Directory.CreateDirectory(screenshotPath);

        var fileStem = $"{capturedAt:yyyy-MM-dd-HHmmss-fff}-snip";
        var filePath = Path.Combine(screenshotPath, $"{fileStem}.png");
        for (var suffix = 2; File.Exists(filePath); suffix++)
        {
            filePath = Path.Combine(screenshotPath, $"{fileStem}-{suffix}.png");
        }

        return filePath;
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Screen snipping is only available on Windows.");
        }
    }
}
