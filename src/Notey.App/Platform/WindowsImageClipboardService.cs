using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Notey.Core.Platform;

namespace Notey.App.Platform;

public sealed class WindowsImageClipboardService(ILogger<WindowsImageClipboardService> logger) : IImageClipboardService
{
    private const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;

    public ValueTask CopyPngAsync(ReadOnlyMemory<byte> pngBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Image clipboard is only available on Windows.");
        }

        if (pngBytes.IsEmpty)
        {
            throw new ArgumentException("PNG bytes cannot be empty.", nameof(pngBytes));
        }

        CopyPngOnUiThread(pngBytes);
        return ValueTask.CompletedTask;
    }

    private void CopyPngOnUiThread(ReadOnlyMemory<byte> pngBytes)
    {
#pragma warning disable CA1416
        using var source = new MemoryStream(pngBytes.ToArray());
        using var image = Image.FromStream(source);
        using var bitmap = new Bitmap(image);
        var dibBytes = EncodeDib(bitmap);
#pragma warning restore CA1416

        if (!OpenClipboard(IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to open the clipboard.");
        }

        try
        {
            if (!EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to empty the clipboard.");
            }

            var handle = GlobalAlloc(GmemMoveable, (UIntPtr)dibBytes.Length);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to allocate clipboard memory.");
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                _ = GlobalFree(handle);
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to lock clipboard memory.");
            }

            try
            {
                Marshal.Copy(dibBytes, 0, pointer, dibBytes.Length);
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }

            if (SetClipboardData(CfDib, handle) == IntPtr.Zero)
            {
                _ = GlobalFree(handle);
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to set clipboard image data.");
            }

            logger.LogInformation("Copied screenshot image ({ByteCount} bytes DIB) to the clipboard.", dibBytes.Length);
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

#pragma warning disable CA1416
    private static byte[] EncodeDib(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Bmp);
        var bmp = stream.ToArray();
        // Strip BITMAPFILEHEADER (14 bytes); CF_DIB expects BITMAPINFOHEADER + pixels.
        const int fileHeaderSize = 14;
        var dib = new byte[bmp.Length - fileHeaderSize];
        Buffer.BlockCopy(bmp, fileHeaderSize, dib, 0, dib.Length);
        return dib;
    }
#pragma warning restore CA1416

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
