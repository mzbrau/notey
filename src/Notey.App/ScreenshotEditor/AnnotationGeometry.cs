namespace Notey.App.ScreenshotEditor;

public static class AnnotationGeometry
{
    public const double HandleSize = 8;
    public const double HitTolerance = 8;

    public static bool HitTest(Annotation annotation, double x, double y)
    {
        return annotation switch
        {
            ArrowAnnotation arrow => DistanceToSegment(x, y, arrow.StartX, arrow.StartY, arrow.EndX, arrow.EndY) <= HitTolerance + arrow.StrokeWidth,
            TextAnnotation text => GetBounds(text).Contains(x, y),
            RectangleAnnotation rect => GetBounds(rect).Contains(x, y),
            HighlightAnnotation highlight => GetBounds(highlight).Contains(x, y),
            BlurAnnotation blur => GetBounds(blur).Contains(x, y),
            _ => false
        };
    }

    public static RectD GetBounds(Annotation annotation)
    {
        return annotation switch
        {
            ArrowAnnotation arrow => NormalizeRect(arrow.StartX, arrow.StartY, arrow.EndX - arrow.StartX, arrow.EndY - arrow.StartY),
            TextAnnotation text => new RectD(text.X, text.Y - text.FontSize, EstimateTextWidth(text), text.FontSize * 1.4),
            RectangleAnnotation rect => NormalizeRect(rect.X, rect.Y, rect.Width, rect.Height),
            HighlightAnnotation highlight => NormalizeRect(highlight.X, highlight.Y, highlight.Width, highlight.Height),
            BlurAnnotation blur => NormalizeRect(blur.X, blur.Y, blur.Width, blur.Height),
            _ => RectD.Empty
        };
    }

    public static IReadOnlyList<HandlePoint> GetHandles(Annotation annotation)
    {
        return annotation switch
        {
            ArrowAnnotation arrow =>
            [
                new HandlePoint(HandleKind.Start, arrow.StartX, arrow.StartY),
                new HandlePoint(HandleKind.End, arrow.EndX, arrow.EndY)
            ],
            TextAnnotation text =>
            [
                new HandlePoint(HandleKind.Move, text.X, text.Y)
            ],
            RectangleAnnotation or HighlightAnnotation or BlurAnnotation => CreateCornerHandles(GetBounds(annotation)),
            _ => []
        };
    }

    public static HandleKind? HitTestHandle(Annotation annotation, double x, double y)
    {
        foreach (var handle in GetHandles(annotation))
        {
            if (Math.Abs(handle.X - x) <= HandleSize && Math.Abs(handle.Y - y) <= HandleSize)
            {
                return handle.Kind;
            }
        }

        return null;
    }

    public static void Move(Annotation annotation, double deltaX, double deltaY)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow:
                arrow.StartX += deltaX;
                arrow.StartY += deltaY;
                arrow.EndX += deltaX;
                arrow.EndY += deltaY;
                break;
            case TextAnnotation text:
                text.X += deltaX;
                text.Y += deltaY;
                break;
            case RectangleAnnotation rect:
                rect.X += deltaX;
                rect.Y += deltaY;
                break;
            case HighlightAnnotation highlight:
                highlight.X += deltaX;
                highlight.Y += deltaY;
                break;
            case BlurAnnotation blur:
                blur.X += deltaX;
                blur.Y += deltaY;
                break;
        }
    }

    public static void ApplyHandleDrag(Annotation annotation, HandleKind handle, double pointerX, double pointerY)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow when handle == HandleKind.Start:
                arrow.StartX = pointerX;
                arrow.StartY = pointerY;
                break;
            case ArrowAnnotation arrow when handle == HandleKind.End:
                arrow.EndX = pointerX;
                arrow.EndY = pointerY;
                break;
            case TextAnnotation text when handle == HandleKind.Move:
                text.X = pointerX;
                text.Y = pointerY;
                break;
            case RectangleAnnotation rect:
            {
                var x = rect.X;
                var y = rect.Y;
                var width = rect.Width;
                var height = rect.Height;
                ResizeRect(ref x, ref y, ref width, ref height, handle, pointerX, pointerY);
                rect.X = x;
                rect.Y = y;
                rect.Width = width;
                rect.Height = height;
                break;
            }
            case HighlightAnnotation highlight:
            {
                var x = highlight.X;
                var y = highlight.Y;
                var width = highlight.Width;
                var height = highlight.Height;
                ResizeRect(ref x, ref y, ref width, ref height, handle, pointerX, pointerY);
                highlight.X = x;
                highlight.Y = y;
                highlight.Width = width;
                highlight.Height = height;
                break;
            }
            case BlurAnnotation blur:
            {
                var x = blur.X;
                var y = blur.Y;
                var width = blur.Width;
                var height = blur.Height;
                ResizeRect(ref x, ref y, ref width, ref height, handle, pointerX, pointerY);
                blur.X = x;
                blur.Y = y;
                blur.Width = width;
                blur.Height = height;
                break;
            }
        }
    }

    public static RectD NormalizeRect(double x, double y, double width, double height)
    {
        if (width < 0)
        {
            x += width;
            width = -width;
        }

        if (height < 0)
        {
            y += height;
            height = -height;
        }

        return new RectD(x, y, width, height);
    }

    public static RectD ClampCrop(RectD crop, int imageWidth, int imageHeight)
    {
        var x = Math.Clamp(crop.X, 0, imageWidth);
        var y = Math.Clamp(crop.Y, 0, imageHeight);
        var right = Math.Clamp(crop.X + crop.Width, 0, imageWidth);
        var bottom = Math.Clamp(crop.Y + crop.Height, 0, imageHeight);
        return new RectD(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    private static HandlePoint[] CreateCornerHandles(RectD bounds) =>
    [
        new HandlePoint(HandleKind.TopLeft, bounds.X, bounds.Y),
        new HandlePoint(HandleKind.TopRight, bounds.X + bounds.Width, bounds.Y),
        new HandlePoint(HandleKind.BottomLeft, bounds.X, bounds.Y + bounds.Height),
        new HandlePoint(HandleKind.BottomRight, bounds.X + bounds.Width, bounds.Y + bounds.Height)
    ];

    private static void ResizeRect(ref double x, ref double y, ref double width, ref double height, HandleKind handle, double px, double py)
    {
        var left = x;
        var top = y;
        var right = x + width;
        var bottom = y + height;

        switch (handle)
        {
            case HandleKind.TopLeft:
                left = px;
                top = py;
                break;
            case HandleKind.TopRight:
                right = px;
                top = py;
                break;
            case HandleKind.BottomLeft:
                left = px;
                bottom = py;
                break;
            case HandleKind.BottomRight:
                right = px;
                bottom = py;
                break;
        }

        var normalized = NormalizeRect(left, top, right - left, bottom - top);
        x = normalized.X;
        y = normalized.Y;
        width = Math.Max(1, normalized.Width);
        height = Math.Max(1, normalized.Height);
    }

    private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        if (dx == 0 && dy == 0)
        {
            return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        }

        var t = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        var projX = x1 + t * dx;
        var projY = y1 + t * dy;
        return Math.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
    }

    private static double EstimateTextWidth(TextAnnotation text)
        => Math.Max(24, text.Text.Length * text.FontSize * 0.55);
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public static RectD Empty { get; } = new(0, 0, 0, 0);

    public bool Contains(double x, double y)
        => x >= X && y >= Y && x <= X + Width && y <= Y + Height;
}

public enum HandleKind
{
    Move,
    Start,
    End,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public readonly record struct HandlePoint(HandleKind Kind, double X, double Y);
