namespace Notey.App.ScreenshotEditor;

public static class PixelateHelper
{
    public static void PixelateBgra(Span<byte> buffer, int width, int height, int stride, int pixelSize)
    {
        if (width <= 0 || height <= 0 || buffer.Length < stride * height)
        {
            return;
        }

        var block = Math.Max(2, pixelSize);
        for (var blockY = 0; blockY < height; blockY += block)
        {
            for (var blockX = 0; blockX < width; blockX += block)
            {
                var blockRight = Math.Min(blockX + block, width);
                var blockBottom = Math.Min(blockY + block, height);
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
    }
}
