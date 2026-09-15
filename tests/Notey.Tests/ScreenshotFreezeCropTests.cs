using System.Drawing;
using System.Drawing.Imaging;
using Notey.App.Platform;
using Notey.App.Views;

namespace Notey.Tests;

public sealed class ScreenshotFreezeCropTests
{
    [Fact]
    public void ClampSelection_keeps_in_bounds_region()
    {
        var bounds = new ScreenSnipSelection(100, 200, 50, 40);
        var clamped = WindowsScreenCaptureHelper.ClampSelection(bounds, new ScreenSnipSelection(110, 210, 12, 8));

        Assert.Equal(110, clamped.X);
        Assert.Equal(210, clamped.Y);
        Assert.Equal(12, clamped.Width);
        Assert.Equal(8, clamped.Height);
    }

    [Fact]
    public void ClampSelection_clips_to_virtual_bounds()
    {
        var bounds = new ScreenSnipSelection(100, 200, 50, 40);
        var clamped = WindowsScreenCaptureHelper.ClampSelection(bounds, new ScreenSnipSelection(140, 230, 30, 30));

        Assert.Equal(140, clamped.X);
        Assert.Equal(230, clamped.Y);
        Assert.Equal(10, clamped.Width);
        Assert.Equal(10, clamped.Height);
    }

    [Fact]
    public void ClampSelection_clips_negative_overflow()
    {
        var bounds = new ScreenSnipSelection(100, 200, 50, 40);
        var clamped = WindowsScreenCaptureHelper.ClampSelection(bounds, new ScreenSnipSelection(80, 180, 40, 30));

        Assert.Equal(100, clamped.X);
        Assert.Equal(200, clamped.Y);
        Assert.Equal(20, clamped.Width);
        Assert.Equal(10, clamped.Height);
    }

    [Fact]
    public void ClampSelection_maps_outside_region_to_nearest_pixel()
    {
        var bounds = new ScreenSnipSelection(100, 200, 50, 40);
        var clamped = WindowsScreenCaptureHelper.ClampSelection(bounds, new ScreenSnipSelection(10, 10, 5, 5));

        Assert.Equal(100, clamped.X);
        Assert.Equal(200, clamped.Y);
        Assert.Equal(1, clamped.Width);
        Assert.Equal(1, clamped.Height);
    }

    [Fact]
    public void CropPngBytes_maps_selection_into_frozen_image()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "System.Drawing GDI+ crop requires Windows.");

        var virtualBounds = new ScreenSnipSelection(100, 200, 8, 4);
        var png = CreateGridPng(8, 4);
        var cropped = WindowsScreenCaptureHelper.CropPngBytes(
            png,
            virtualBounds,
            new ScreenSnipSelection(103, 201, 3, 2));

#pragma warning disable CA1416
        using var bitmap = new Bitmap(new MemoryStream(cropped));
        Assert.Equal(3, bitmap.Width);
        Assert.Equal(2, bitmap.Height);
        Assert.Equal(Color.FromArgb(255, 3, 1, 0), bitmap.GetPixel(0, 0));
        Assert.Equal(Color.FromArgb(255, 5, 2, 0), bitmap.GetPixel(2, 1));
#pragma warning restore CA1416
    }

    [Fact]
    public void CropPngBytes_clamps_selection_that_extends_past_bounds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "System.Drawing GDI+ crop requires Windows.");

        var virtualBounds = new ScreenSnipSelection(-10, 0, 6, 3);
        var png = CreateGridPng(6, 3);
        var cropped = WindowsScreenCaptureHelper.CropPngBytes(
            png,
            virtualBounds,
            new ScreenSnipSelection(-8, 1, 20, 20));

#pragma warning disable CA1416
        using var bitmap = new Bitmap(new MemoryStream(cropped));
        Assert.Equal(4, bitmap.Width);
        Assert.Equal(2, bitmap.Height);
        Assert.Equal(Color.FromArgb(255, 2, 1, 0), bitmap.GetPixel(0, 0));
        Assert.Equal(Color.FromArgb(255, 5, 2, 0), bitmap.GetPixel(3, 1));
#pragma warning restore CA1416
    }

    private static byte[] CreateGridPng(int width, int height)
    {
#pragma warning disable CA1416
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, Color.FromArgb(255, x, y, 0));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
#pragma warning restore CA1416
    }
}
