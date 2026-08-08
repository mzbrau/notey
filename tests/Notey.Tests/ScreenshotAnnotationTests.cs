using Notey.App.ScreenshotEditor;
using Notey.Core.Configuration;
using Notey.Core.Platform;

namespace Notey.Tests;

public sealed class ScreenshotAnnotationTests
{
    [Fact]
    public void Hotkey_defaults_include_screenshot_gestures()
    {
        var options = new NoteyOptions();

        Assert.Equal("Ctrl+Alt+3", options.Hotkeys.CaptureFullScreen);
        Assert.Equal("Ctrl+Alt+4", options.Hotkeys.CaptureRegionClipboard);
        Assert.Equal("Ctrl+Alt+5", options.Hotkeys.CaptureRegionEditor);
        Assert.Equal("Ctrl+Alt+6", options.Hotkeys.CaptureWindowEditor);

        Assert.Equal("3", HotkeyGesture.Parse(options.Hotkeys.CaptureFullScreen).Key);
        Assert.Equal("4", HotkeyGesture.Parse(options.Hotkeys.CaptureRegionClipboard).Key);
        Assert.Equal("5", HotkeyGesture.Parse(options.Hotkeys.CaptureRegionEditor).Key);
        Assert.Equal("6", HotkeyGesture.Parse(options.Hotkeys.CaptureWindowEditor).Key);
    }

    [Fact]
    public void HitTest_finds_arrow_near_segment()
    {
        var arrow = new ArrowAnnotation
        {
            StartX = 10,
            StartY = 10,
            EndX = 110,
            EndY = 10
        };

        Assert.True(AnnotationGeometry.HitTest(arrow, 60, 12));
        Assert.False(AnnotationGeometry.HitTest(arrow, 60, 40));
    }

    [Fact]
    public void Move_and_handle_drag_update_rectangle_bounds()
    {
        var rect = new RectangleAnnotation { X = 10, Y = 20, Width = 40, Height = 30 };
        AnnotationGeometry.Move(rect, 5, -5);
        Assert.Equal(15, rect.X);
        Assert.Equal(15, rect.Y);

        AnnotationGeometry.ApplyHandleDrag(rect, HandleKind.BottomRight, 80, 70);
        var bounds = AnnotationGeometry.GetBounds(rect);
        Assert.Equal(15, bounds.X);
        Assert.Equal(15, bounds.Y);
        Assert.Equal(65, bounds.Width);
        Assert.Equal(55, bounds.Height);
    }

    [Fact]
    public void ClampCrop_and_replace_base_image_clear_annotations()
    {
        var clamped = AnnotationGeometry.ClampCrop(new RectD(-10, 5, 80, 40), 120, 80);
        Assert.Equal(0, clamped.X);
        Assert.Equal(5, clamped.Y);
        Assert.Equal(70, clamped.Width);
        Assert.Equal(40, clamped.Height);

        var document = new ScreenshotEditDocument([0x89, 0x50, 0x4E, 0x47], 120, 80);
        document.Annotations.Add(new RectangleAnnotation { X = 5, Y = 5, Width = 20, Height = 20 });
        document.ReplaceBaseImage([0x89, 0x50, 0x4E, 0x47], 50, 40);

        Assert.Empty(document.Annotations);
        Assert.Equal(50, document.Width);
        Assert.Equal(40, document.Height);
    }

    [Fact]
    public void Compositor_crop_and_flatten_run_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "System.Drawing GDI+ compositor requires Windows.");

#pragma warning disable CA1416
        using var bitmap = new System.Drawing.Bitmap(64, 48, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.White);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var png = stream.ToArray();
#pragma warning restore CA1416

        var document = new ScreenshotEditDocument(png, 64, 48);
        document.Annotations.Add(new ArrowAnnotation
        {
            StartX = 4,
            StartY = 4,
            EndX = 40,
            EndY = 30,
            ColorArgb = 0xFFFF0000
        });

        var flattened = AnnotationCompositor.FlattenToPng(document);
        Assert.NotEmpty(flattened);

        var (croppedPng, width, height) = AnnotationCompositor.Crop(document, new RectD(10, 10, 30, 20));
        Assert.Equal(30, width);
        Assert.Equal(20, height);
        Assert.NotEmpty(croppedPng);
    }
}
