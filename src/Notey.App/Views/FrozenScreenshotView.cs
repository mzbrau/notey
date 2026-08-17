using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Notey.App.Views;

internal static class FrozenScreenshotView
{
    public static Bitmap DecodePng(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        return new Bitmap(stream);
    }

    public static Panel CreateHost(
        IImage frozen,
        ScreenSnipSelection virtualBounds,
        PixelRect? screenBounds,
        Control overlay)
    {
        var image = new Image
        {
            Source = CreateMonitorSource(frozen, virtualBounds, screenBounds),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };

        var host = new Panel();
        host.Children.Add(image);
        host.Children.Add(overlay);
        return host;
    }

    private static IImage CreateMonitorSource(IImage frozen, ScreenSnipSelection virtualBounds, PixelRect? screenBounds)
    {
        if (frozen is not Bitmap bitmap || screenBounds is not { } bounds)
        {
            return frozen;
        }

        var x = bounds.X - virtualBounds.X;
        var y = bounds.Y - virtualBounds.Y;
        if (x < 0 || y < 0 || bounds.Width <= 0 || bounds.Height <= 0
            || x >= bitmap.PixelSize.Width || y >= bitmap.PixelSize.Height)
        {
            return frozen;
        }

        var width = Math.Min(bounds.Width, bitmap.PixelSize.Width - x);
        var height = Math.Min(bounds.Height, bitmap.PixelSize.Height - y);
        if (x == 0 && y == 0 && width == bitmap.PixelSize.Width && height == bitmap.PixelSize.Height)
        {
            return frozen;
        }

        return new CroppedBitmap(bitmap, new PixelRect(x, y, width, height));
    }
}
