using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Notey.App.ScreenshotEditor;

public static class ImagePixelOps
{
    public static uint SamplePngPixel(byte[] pngBytes, int x, int y)
    {
#pragma warning disable CA1416
        using var stream = new MemoryStream(pngBytes);
        using var bitmap = new Bitmap(stream);
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
        {
            return 0xFF000000;
        }

        var color = bitmap.GetPixel(x, y);
        return (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
#pragma warning restore CA1416
    }

    public static byte[] FloodFillPng(byte[] pngBytes, int startX, int startY, uint fillColorArgb)
    {
#pragma warning disable CA1416
        using var stream = new MemoryStream(pngBytes);
        using var source = new Bitmap(stream);
        using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        if (startX < 0 || startY < 0 || startX >= bitmap.Width || startY >= bitmap.Height)
        {
            return pngBytes;
        }

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var buffer = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            var targetIndex = (startY * stride) + (startX * 4);
            var targetB = buffer[targetIndex];
            var targetG = buffer[targetIndex + 1];
            var targetR = buffer[targetIndex + 2];
            var targetA = buffer[targetIndex + 3];

            var fillB = (byte)(fillColorArgb & 0xFF);
            var fillG = (byte)((fillColorArgb >> 8) & 0xFF);
            var fillR = (byte)((fillColorArgb >> 16) & 0xFF);
            var fillA = (byte)((fillColorArgb >> 24) & 0xFF);

            if (targetB == fillB && targetG == fillG && targetR == fillR && targetA == fillA)
            {
                return pngBytes;
            }

            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue((startX, startY));
            var visited = new bool[bitmap.Width * bitmap.Height];
            visited[(startY * bitmap.Width) + startX] = true;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                var index = (y * stride) + (x * 4);
                if (buffer[index] != targetB
                    || buffer[index + 1] != targetG
                    || buffer[index + 2] != targetR
                    || buffer[index + 3] != targetA)
                {
                    continue;
                }

                buffer[index] = fillB;
                buffer[index + 1] = fillG;
                buffer[index + 2] = fillR;
                buffer[index + 3] = fillA;

                TryEnqueue(x - 1, y);
                TryEnqueue(x + 1, y);
                TryEnqueue(x, y - 1);
                TryEnqueue(x, y + 1);
            }

            void TryEnqueue(int x, int y)
            {
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
                {
                    return;
                }

                var key = (y * bitmap.Width) + x;
                if (visited[key])
                {
                    return;
                }

                visited[key] = true;
                queue.Enqueue((x, y));
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
#pragma warning restore CA1416
    }

    public static byte[]? CreatePixelatedRegionPng(byte[] pngBytes, RectD region, int pixelSize, out int width, out int height)
    {
        width = 0;
        height = 0;
#pragma warning disable CA1416
        using var stream = new MemoryStream(pngBytes);
        using var source = new Bitmap(stream);
        var x = Math.Clamp((int)Math.Floor(region.X), 0, source.Width - 1);
        var y = Math.Clamp((int)Math.Floor(region.Y), 0, source.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(region.X + region.Width), 0, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Y + region.Height), 0, source.Height);
        if (right <= x || bottom <= y)
        {
            return null;
        }

        width = right - x;
        height = bottom - y;
        using var cropped = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(cropped))
        {
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                new Rectangle(x, y, width, height),
                GraphicsUnit.Pixel);
        }

        var rect = new Rectangle(0, 0, width, height);
        var data = cropped.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var buffer = new byte[stride * height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            PixelateHelper.PixelateBgra(buffer, width, height, stride, pixelSize);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            cropped.UnlockBits(data);
        }

        using var output = new MemoryStream();
        cropped.Save(output, ImageFormat.Png);
        return output.ToArray();
#pragma warning restore CA1416
    }
}
