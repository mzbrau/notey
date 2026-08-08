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

#pragma warning disable CA1416
        using var sourceStream = new MemoryStream(document.PngBytes);
        using var source = Image.FromStream(sourceStream);
        var width = Math.Max(1, (int)Math.Round(clamped.Width));
        var height = Math.Max(1, (int)Math.Round(clamped.Height));
        using var cropped = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(cropped))
        {
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                new Rectangle(
                    (int)Math.Round(clamped.X),
                    (int)Math.Round(clamped.Y),
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
                using (var brush = new SolidBrush(Color.FromArgb(unchecked((int)text.ColorArgb))))
                using (var font = new Font("Segoe UI", (float)Math.Max(8, text.FontSize), FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    graphics.DrawString(text.Text, font, brush, (float)text.X, (float)(text.Y - text.FontSize));
                }

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
        }
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
        var block = Math.Max(2, pixelSize);

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

            for (var blockY = 0; blockY < rect.Height; blockY += block)
            {
                for (var blockX = 0; blockX < rect.Width; blockX += block)
                {
                    var blockRight = Math.Min(blockX + block, rect.Width);
                    var blockBottom = Math.Min(blockY + block, rect.Height);
                    long r = 0, g = 0, b = 0, a = 0, count = 0;

                    for (var py = blockY; py < blockBottom; py++)
                    {
                        var row = py * stride;
                        for (var px = blockX; px < blockRight; px++)
                        {
                            var index = row + (px * 4);
                            b += buffer[index];
                            g += buffer[index + 1];
                            r += buffer[index + 2];
                            a += buffer[index + 3];
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        continue;
                    }

                    var averageB = (byte)(b / count);
                    var averageG = (byte)(g / count);
                    var averageR = (byte)(r / count);
                    var averageA = (byte)(a / count);

                    for (var py = blockY; py < blockBottom; py++)
                    {
                        var row = py * stride;
                        for (var px = blockX; px < blockRight; px++)
                        {
                            var index = row + (px * 4);
                            buffer[index] = averageB;
                            buffer[index + 1] = averageG;
                            buffer[index + 2] = averageR;
                            buffer[index + 3] = averageA;
                        }
                    }
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
#pragma warning restore CA1416
}
