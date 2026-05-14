using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Notey.App.Views;
using Notey.Capture.Abstractions;
using Notey.Vault.Abstractions;

namespace Notey.App.Platform;

public sealed class WindowsScreenSnipService(
    IVaultWorkspace workspace,
    TimeProvider timeProvider,
    ILogger<WindowsScreenSnipService> logger) : IScreenSnipService
{
    private const int Srccopy = 0x00CC0020;

    public async ValueTask<ScreenSnipResult> CaptureAsync(ScreenSnipMode mode, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Screen snipping is only available on Windows.");
        }

        var selection = await ScreenSnipSelectionWindow.ShowSelectionAsync(cancellationToken);
        if (selection is null)
        {
            throw new OperationCanceledException("Screen snip selection was cancelled.", cancellationToken);
        }

        var capturedAt = timeProvider.GetLocalNow();
        var filePath = GetUniqueSnipPath(capturedAt);
        var succeeded = false;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(125), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => CaptureRegionToPng(selection, filePath), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            succeeded = true;
        }
        finally
        {
            if (!succeeded)
            {
                DeleteIncompleteSnip(filePath);
            }
        }

        logger.LogInformation(
            "Saved screen snip {FilePath} ({Width}x{Height}).",
            filePath,
            selection.Width,
            selection.Height);

        return new ScreenSnipResult(filePath, capturedAt, selection.Width, selection.Height, mode);
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

    private void DeleteIncompleteSnip(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to delete cancelled screen snip {FilePath}.", filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Notey does not have permission to delete cancelled screen snip {FilePath}.", filePath);
        }
    }

    private static void CaptureRegionToPng(ScreenSnipSelection selection, string filePath)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get the screen device context.");
        }

        var memoryDc = IntPtr.Zero;
        var bitmapHandle = IntPtr.Zero;
        var previousObject = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create a compatible device context.");
            }

            bitmapHandle = CreateCompatibleBitmap(screenDc, selection.Width, selection.Height);
            if (bitmapHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create a compatible bitmap.");
            }

            previousObject = SelectObject(memoryDc, bitmapHandle);
            if (previousObject == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to select the capture bitmap.");
            }

            if (!BitBlt(memoryDc, 0, 0, selection.Width, selection.Height, screenDc, selection.X, selection.Y, Srccopy))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to capture the selected screen region.");
            }

#pragma warning disable CA1416
            using var image = Image.FromHbitmap(bitmapHandle);
            image.Save(filePath, ImageFormat.Png);
#pragma warning restore CA1416
        }
        finally
        {
            if (previousObject != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                _ = SelectObject(memoryDc, previousObject);
            }

            if (bitmapHandle != IntPtr.Zero)
            {
                _ = DeleteObject(bitmapHandle);
            }

            if (memoryDc != IntPtr.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hGdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr destinationDc,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        int rasterOperation);
}
