using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Notey.App.Views;

namespace Notey.App.Platform;

internal static class WindowsScreenCaptureHelper
{
    private const int Srccopy = 0x00CC0020;
    private static readonly IntPtr HgdiError = new(-1);

    public static byte[] CaptureRegionToPngBytes(ScreenSnipSelection selection)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to get the screen device context.");
        }

        var memoryDc = IntPtr.Zero;
        var bitmapHandle = IntPtr.Zero;
        var previousObject = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to create a compatible device context.");
            }

            bitmapHandle = CreateCompatibleBitmap(screenDc, selection.Width, selection.Height);
            if (bitmapHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to create a compatible bitmap.");
            }

            var selectedObject = SelectObject(memoryDc, bitmapHandle);
            if (selectedObject == HgdiError)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to select the capture bitmap.");
            }

            previousObject = selectedObject;

            if (!BitBlt(memoryDc, 0, 0, selection.Width, selection.Height, screenDc, selection.X, selection.Y, Srccopy))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to capture the selected screen region.");
            }

#pragma warning disable CA1416
            using var image = Image.FromHbitmap(bitmapHandle);
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            return stream.ToArray();
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

    public static void CaptureRegionToPngFile(ScreenSnipSelection selection, string filePath)
    {
        var bytes = CaptureRegionToPngBytes(selection);
        File.WriteAllBytes(filePath, bytes);
    }

    public static ScreenSnipSelection GetVirtualScreenBounds()
    {
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to read virtual screen bounds.");
        }

        return new ScreenSnipSelection(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            width,
            height);
    }

    public static byte[] CropPngBytes(byte[] frozenPng, ScreenSnipSelection virtualBounds, ScreenSnipSelection selection)
    {
        ArgumentNullException.ThrowIfNull(frozenPng);
        if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualBounds), "Virtual bounds must be positive.");
        }

        var crop = ClampSelection(virtualBounds, selection);

#pragma warning disable CA1416
        using var stream = new MemoryStream(frozenPng);
        using var source = new Bitmap(stream);
        var sourceX = Math.Clamp(crop.X - virtualBounds.X, 0, Math.Max(0, source.Width - 1));
        var sourceY = Math.Clamp(crop.Y - virtualBounds.Y, 0, Math.Max(0, source.Height - 1));
        var width = Math.Clamp(crop.Width, 1, Math.Max(1, source.Width - sourceX));
        var height = Math.Clamp(crop.Height, 1, Math.Max(1, source.Height - sourceY));

        using var cropped = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(cropped))
        {
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                new Rectangle(sourceX, sourceY, width, height),
                GraphicsUnit.Pixel);
        }

        using var output = new MemoryStream();
        cropped.Save(output, ImageFormat.Png);
        return output.ToArray();
#pragma warning restore CA1416
    }

    public static ScreenSnipSelection ClampSelection(ScreenSnipSelection bounds, ScreenSnipSelection selection)
    {
        var boundsLeft = bounds.X;
        var boundsTop = bounds.Y;
        var boundsRight = bounds.X + Math.Max(1, bounds.Width);
        var boundsBottom = bounds.Y + Math.Max(1, bounds.Height);

        var left = Math.Max(boundsLeft, selection.X);
        var top = Math.Max(boundsTop, selection.Y);
        var right = Math.Min(boundsRight, selection.X + Math.Max(1, selection.Width));
        var bottom = Math.Min(boundsBottom, selection.Y + Math.Max(1, selection.Height));

        if (right <= left || bottom <= top)
        {
            left = Math.Clamp(selection.X, boundsLeft, boundsRight - 1);
            top = Math.Clamp(selection.Y, boundsTop, boundsBottom - 1);
            right = Math.Min(left + 1, boundsRight);
            bottom = Math.Min(top + 1, boundsBottom);
        }

        return new ScreenSnipSelection(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public static ScreenSnipSelection GetMonitorBoundsUnderCursor()
    {
        if (!GetCursorPos(out var point))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to read the cursor position.");
        }

        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to resolve the monitor under the cursor.");
        }

        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to read monitor bounds.");
        }

        var width = info.Monitor.Right - info.Monitor.Left;
        var height = info.Monitor.Bottom - info.Monitor.Top;
        return new ScreenSnipSelection(info.Monitor.Left, info.Monitor.Top, width, height);
    }

    private const uint MonitorDefaultToNearest = 2;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

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
