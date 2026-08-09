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
    public void HitTestHandle_matches_rendered_handle_size()
    {
        var arrow = new ArrowAnnotation
        {
            StartX = 20,
            StartY = 30,
            EndX = 80,
            EndY = 30
        };

        var half = AnnotationGeometry.HandleSize / 2;
        Assert.Equal(HandleKind.Start, AnnotationGeometry.HitTestHandle(arrow, 20 + half, 30));
        Assert.Equal(HandleKind.End, AnnotationGeometry.HitTestHandle(arrow, 80 - half, 30));
        Assert.Null(AnnotationGeometry.HitTestHandle(arrow, 20 + half + 1, 30));
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
    public void Crop_retains_and_translates_in_bounds_annotations()
    {
        var clamped = AnnotationGeometry.ClampCrop(new RectD(-10, 5, 80, 40), 120, 80);
        Assert.Equal(0, clamped.X);
        Assert.Equal(5, clamped.Y);
        Assert.Equal(70, clamped.Width);
        Assert.Equal(40, clamped.Height);

        var inside = new RectangleAnnotation { X = 10, Y = 10, Width = 20, Height = 20 };
        var outside = new RectangleAnnotation { X = 100, Y = 60, Width = 10, Height = 10 };
        var arrow = new ArrowAnnotation { StartX = 5, StartY = 10, EndX = 40, EndY = 20 };
        var kept = AnnotationGeometry.TransformAnnotationsForCrop([inside, outside, arrow], clamped);

        Assert.Equal(2, kept.Count);
        var keptRect = Assert.IsType<RectangleAnnotation>(kept[0]);
        Assert.Equal(10, keptRect.X);
        Assert.Equal(5, keptRect.Y);
        var keptArrow = Assert.IsType<ArrowAnnotation>(kept[1]);
        Assert.Equal(5, keptArrow.StartX);
        Assert.Equal(5, keptArrow.StartY);
    }

    [Fact]
    public void Undo_restores_previous_annotation_state()
    {
        var document = new ScreenshotEditDocument([0x89, 0x50, 0x4E, 0x47], 120, 80);
        var history = new ScreenshotEditHistory();
        history.Push(document);

        document.Annotations.Add(new RectangleAnnotation { X = 5, Y = 5, Width = 20, Height = 20 });
        Assert.Single(document.Annotations);

        Assert.True(history.TryUndo(document));
        Assert.Empty(document.Annotations);
    }

    [Fact]
    public void Redo_restores_undone_annotation_state()
    {
        var document = new ScreenshotEditDocument([0x89, 0x50, 0x4E, 0x47], 120, 80);
        var history = new ScreenshotEditHistory();
        history.Push(document);

        document.Annotations.Add(new RectangleAnnotation { X = 5, Y = 5, Width = 20, Height = 20 });
        Assert.True(history.TryUndo(document));
        Assert.Empty(document.Annotations);
        Assert.True(history.CanRedo);

        Assert.True(history.TryRedo(document));
        Assert.Single(document.Annotations);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Pen_hit_test_and_move()
    {
        var pen = new PenAnnotation { StrokeWidth = 2 };
        pen.Points.Add(new PointD(10, 10));
        pen.Points.Add(new PointD(40, 10));
        pen.Points.Add(new PointD(40, 30));

        Assert.True(AnnotationGeometry.HitTest(pen, 25, 12));
        Assert.False(AnnotationGeometry.HitTest(pen, 10, 40));

        AnnotationGeometry.Move(pen, 5, 5);
        Assert.Equal(new PointD(15, 15), pen.Points[0]);
        Assert.Equal(new PointD(45, 15), pen.Points[1]);
    }

    [Fact]
    public void Text_clone_preserves_formatting_and_id()
    {
        var text = new TextAnnotation
        {
            Text = "Hello\nWorld",
            FontSize = 18,
            IsBold = true,
            IsItalic = true,
            X = 4,
            Y = 8,
            ColorArgb = 0xFF112233,
            BackgroundColorArgb = 0xCCF5F5F5
        };

        var clone = Assert.IsType<TextAnnotation>(text.Clone());
        Assert.Equal(text.Id, clone.Id);
        Assert.Equal(text.Text, clone.Text);
        Assert.Equal(18, clone.FontSize);
        Assert.True(clone.IsBold);
        Assert.True(clone.IsItalic);
        Assert.Equal(0xFF112233u, clone.ColorArgb);
        Assert.Equal(0xCCF5F5F5u, clone.BackgroundColorArgb);
    }

    [Fact]
    public void PixelateHelper_averages_blocks()
    {
        const int width = 4;
        const int height = 4;
        const int stride = width * 4;
        var buffer = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * stride) + (x * 4);
                buffer[index] = (byte)(x * 10);
                buffer[index + 1] = (byte)(y * 10);
                buffer[index + 2] = 100;
                buffer[index + 3] = 255;
            }
        }

        PixelateHelper.PixelateBgra(buffer, width, height, stride, pixelSize: 4);
        Assert.Equal(buffer[0], buffer[4]);
        Assert.Equal(buffer[1], buffer[stride + 1]);
        Assert.Equal((byte)255, buffer[3]);
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
        document.Annotations.Add(new PenAnnotation
        {
            ColorArgb = 0xFF00FF00,
            StrokeWidth = 2,
            Points = { new PointD(8, 8), new PointD(20, 18) }
        });
        document.Annotations.Add(new TextAnnotation
        {
            Text = "Hi",
            IsBold = true,
            FontSize = 16,
            X = 12,
            Y = 24,
            ColorArgb = 0xFF0000FF
        });

        var flattened = AnnotationCompositor.FlattenToPng(document);
        Assert.NotEmpty(flattened);

        var (croppedPng, width, height) = AnnotationCompositor.Crop(document, new RectD(10, 10, 30, 20));
        Assert.Equal(30, width);
        Assert.Equal(20, height);
        Assert.NotEmpty(croppedPng);

        document.ApplyCrop(new RectD(10, 10, 30, 20));
        Assert.Equal(30, document.Width);
        Assert.Equal(20, document.Height);
        Assert.NotEmpty(document.Annotations);
    }

    [Fact]
    public void FloodFill_respects_color_tolerance()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "System.Drawing GDI+ pixel ops require Windows.");

#pragma warning disable CA1416
        using var bitmap = new System.Drawing.Bitmap(4, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, System.Drawing.Color.FromArgb(255, 100, 100, 100));
        bitmap.SetPixel(1, 0, System.Drawing.Color.FromArgb(255, 110, 100, 100));
        bitmap.SetPixel(2, 0, System.Drawing.Color.FromArgb(255, 140, 100, 100));
        bitmap.SetPixel(3, 0, System.Drawing.Color.FromArgb(255, 0, 0, 255));
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var png = stream.ToArray();
#pragma warning restore CA1416

        var exact = ImagePixelOps.FloodFillPng(png, 0, 0, 0xFFFF0000, tolerance: 0);
#pragma warning disable CA1416
        using (var exactBitmap = new System.Drawing.Bitmap(new MemoryStream(exact)))
        {
            Assert.Equal(System.Drawing.Color.FromArgb(255, 255, 0, 0), exactBitmap.GetPixel(0, 0));
            Assert.Equal(System.Drawing.Color.FromArgb(255, 110, 100, 100), exactBitmap.GetPixel(1, 0));
        }

        var tolerant = ImagePixelOps.FloodFillPng(png, 0, 0, 0xFFFF0000, tolerance: 15);
        using (var tolerantBitmap = new System.Drawing.Bitmap(new MemoryStream(tolerant)))
        {
            Assert.Equal(System.Drawing.Color.FromArgb(255, 255, 0, 0), tolerantBitmap.GetPixel(0, 0));
            Assert.Equal(System.Drawing.Color.FromArgb(255, 255, 0, 0), tolerantBitmap.GetPixel(1, 0));
            Assert.Equal(System.Drawing.Color.FromArgb(255, 140, 100, 100), tolerantBitmap.GetPixel(2, 0));
            Assert.Equal(System.Drawing.Color.FromArgb(255, 0, 0, 255), tolerantBitmap.GetPixel(3, 0));
        }
#pragma warning restore CA1416
    }
}
