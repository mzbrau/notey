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

    public static byte[] CaptureWindowToPngBytes(WindowCaptureSelection selection)
    {
        var bounds = selection.Bounds;
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

            bitmapHandle = CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
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

            var printed = PrintWindow(selection.Hwnd, memoryDc, PwRenderFullContent);
            if (!printed)
            {
                // Some windows reject PrintWindow; fall back to a screen BitBlt of the window bounds.
                if (!BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y, Srccopy))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to capture the selected window.");
                }
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
    private const uint PwRenderFullContent = 0x00000002;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);
}
