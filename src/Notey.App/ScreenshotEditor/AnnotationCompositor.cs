using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Notey.App.ScreenshotEditor;

public static class AnnotationCompositor
{
    public static byte[] FlattenToPng(ScreenshotEditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

#pragma warning disable CA1416
        using var sourceStream = new MemoryStream(document.PngBytes);
        using var source = Image.FromStream(sourceStream);
        using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);

            foreach (var annotation in document.Annotations)
            {
                DrawAnnotation(graphics, bitmap, annotation);
            }
        }

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
#pragma warning restore CA1416
    }

    public static (byte[] PngBytes, int Width, int Height) Crop(ScreenshotEditDocument document, RectD crop)
    {
        ArgumentNullException.ThrowIfNull(document);
        var clamped = AnnotationGeometry.ClampCrop(crop, document.Width, document.Height);
        return CropBaseImage(document.PngBytes, clamped);
    }

    public static (byte[] PngBytes, int Width, int Height) CropBaseImage(byte[] pngBytes, RectD crop)
    {
#pragma warning disable CA1416
        using var sourceStream = new MemoryStream(pngBytes);
        using var source = Image.FromStream(sourceStream);
        var width = Math.Max(1, (int)Math.Round(crop.Width));
        var height = Math.Max(1, (int)Math.Round(crop.Height));
        using var cropped = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(cropped))
        {
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                new Rectangle(
                    (int)Math.Round(crop.X),
                    (int)Math.Round(crop.Y),
                    width,
                    height),
                GraphicsUnit.Pixel);
        }

        using var output = new MemoryStream();
        cropped.Save(output, ImageFormat.Png);
        return (output.ToArray(), width, height);
#pragma warning restore CA1416
    }

#pragma warning disable CA1416
    private static void DrawAnnotation(Graphics graphics, Bitmap bitmap, Annotation annotation)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow:
                DrawArrow(graphics, arrow);
                break;
            case TextAnnotation text:
                DrawText(graphics, text);
                break;
            case RectangleAnnotation rect:
                using (var pen = new Pen(Color.FromArgb(unchecked((int)rect.ColorArgb)), (float)rect.StrokeWidth))
                {
                    var bounds = AnnotationGeometry.NormalizeRect(rect.X, rect.Y, rect.Width, rect.Height);
                    graphics.DrawRectangle(pen, (float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
                }

                break;
            case HighlightAnnotation highlight:
                using (var brush = new SolidBrush(Color.FromArgb(highlight.Opacity, Color.FromArgb(unchecked((int)highlight.ColorArgb)))))
                {
                    var bounds = AnnotationGeometry.NormalizeRect(highlight.X, highlight.Y, highlight.Width, highlight.Height);
                    graphics.FillRectangle(brush, (float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
                }

                break;
            case BlurAnnotation blur:
                ApplyPixelate(bitmap, AnnotationGeometry.NormalizeRect(blur.X, blur.Y, blur.Width, blur.Height), blur.PixelSize);
                break;
            case PenAnnotation pen:
                DrawPen(graphics, pen);
                break;
        }
    }

    private static void DrawText(Graphics graphics, TextAnnotation text)
    {
        var style = FontStyle.Regular;
        if (text.IsBold)
        {
            style |= FontStyle.Bold;
        }

        if (text.IsItalic)
        {
            style |= FontStyle.Italic;
        }

        using var brush = new SolidBrush(Color.FromArgb(unchecked((int)text.ColorArgb)));
        using var font = new Font("Segoe UI", (float)Math.Max(8, text.FontSize), style, GraphicsUnit.Pixel);
        graphics.DrawString(text.Text, font, brush, (float)text.X, (float)(text.Y - text.FontSize));
    }

    private static void DrawPen(Graphics graphics, PenAnnotation pen)
    {
        if (pen.Points.Count == 0)
        {
            return;
        }

        using var gdiPen = new Pen(Color.FromArgb(unchecked((int)pen.ColorArgb)), (float)pen.StrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        if (pen.Points.Count == 1)
        {
            var point = pen.Points[0];
            graphics.DrawEllipse(
                gdiPen,
                (float)(point.X - pen.StrokeWidth / 2),
                (float)(point.Y - pen.StrokeWidth / 2),
                (float)pen.StrokeWidth,
                (float)pen.StrokeWidth);
            return;
        }

        var points = pen.Points.Select(static point => new PointF((float)point.X, (float)point.Y)).ToArray();
        graphics.DrawLines(gdiPen, points);
    }

    private static void DrawArrow(Graphics graphics, ArrowAnnotation arrow)
    {
        using var pen = new Pen(Color.FromArgb(unchecked((int)arrow.ColorArgb)), (float)arrow.StrokeWidth)
        {
            EndCap = LineCap.Round,
            StartCap = LineCap.Round
        };
        graphics.DrawLine(pen, (float)arrow.StartX, (float)arrow.StartY, (float)arrow.EndX, (float)arrow.EndY);

        var angle = Math.Atan2(arrow.EndY - arrow.StartY, arrow.EndX - arrow.StartX);
        var headLength = 14 + arrow.StrokeWidth * 2;
        var leftX = arrow.EndX - headLength * Math.Cos(angle - Math.PI / 6);
        var leftY = arrow.EndY - headLength * Math.Sin(angle - Math.PI / 6);
        var rightX = arrow.EndX - headLength * Math.Cos(angle + Math.PI / 6);
        var rightY = arrow.EndY - headLength * Math.Sin(angle + Math.PI / 6);
        graphics.DrawLine(pen, (float)arrow.EndX, (float)arrow.EndY, (float)leftX, (float)leftY);
        graphics.DrawLine(pen, (float)arrow.EndX, (float)arrow.EndY, (float)rightX, (float)rightY);
    }

    private static void ApplyPixelate(Bitmap bitmap, RectD region, int pixelSize)
    {
        var x = Math.Clamp((int)Math.Floor(region.X), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)Math.Floor(region.Y), 0, bitmap.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(region.X + region.Width), 0, bitmap.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Y + region.Height), 0, bitmap.Height);

        if (right <= x || bottom <= y)
        {
            return;
        }

        var rect = new Rectangle(x, y, right - x, bottom - y);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var buffer = new byte[stride * rect.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            PixelateHelper.PixelateBgra(buffer, rect.Width, rect.Height, stride, pixelSize);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
#pragma warning restore CA1416
}
