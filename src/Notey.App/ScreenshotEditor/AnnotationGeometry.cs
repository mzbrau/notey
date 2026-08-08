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
            PenAnnotation pen => HitTestPen(pen, x, y),
            _ => false
        };
    }

    public static RectD GetBounds(Annotation annotation)
    {
        return annotation switch
        {
            ArrowAnnotation arrow => NormalizeRect(arrow.StartX, arrow.StartY, arrow.EndX - arrow.StartX, arrow.EndY - arrow.StartY),
            TextAnnotation text => EstimateTextBounds(text),
            RectangleAnnotation rect => NormalizeRect(rect.X, rect.Y, rect.Width, rect.Height),
            HighlightAnnotation highlight => NormalizeRect(highlight.X, highlight.Y, highlight.Width, highlight.Height),
            BlurAnnotation blur => NormalizeRect(blur.X, blur.Y, blur.Width, blur.Height),
            PenAnnotation pen => GetPenBounds(pen),
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
            PenAnnotation pen when pen.Points.Count > 0 =>
            [
                new HandlePoint(HandleKind.Move, pen.Points[0].X, pen.Points[0].Y)
            ],
            _ => []
        };
    }

    public static HandleKind? HitTestHandle(Annotation annotation, double x, double y)
    {
        foreach (var handle in GetHandles(annotation))
        {
            if (Math.Abs(handle.X - x) <= HandleSize / 2 && Math.Abs(handle.Y - y) <= HandleSize / 2)
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
            case PenAnnotation pen:
                for (var index = 0; index < pen.Points.Count; index++)
                {
                    var point = pen.Points[index];
                    pen.Points[index] = new PointD(point.X + deltaX, point.Y + deltaY);
                }

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
            case PenAnnotation pen when handle == HandleKind.Move && pen.Points.Count > 0:
            {
                var origin = pen.Points[0];
                Move(pen, pointerX - origin.X, pointerY - origin.Y);
                break;
            }
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

    public static IReadOnlyList<Annotation> TransformAnnotationsForCrop(IEnumerable<Annotation> annotations, RectD crop)
    {
        var kept = new List<Annotation>();
        foreach (var annotation in annotations)
        {
            var clone = annotation.Clone();
            if (!TryTransformForCrop(clone, crop))
            {
                continue;
            }

            kept.Add(clone);
        }

        return kept;
    }

    private static bool TryTransformForCrop(Annotation annotation, RectD crop)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow:
            {
                var startInside = crop.Contains(arrow.StartX, arrow.StartY);
                var endInside = crop.Contains(arrow.EndX, arrow.EndY);
                if (!startInside && !endInside && !SegmentIntersectsRect(arrow.StartX, arrow.StartY, arrow.EndX, arrow.EndY, crop))
                {
                    return false;
                }

                arrow.StartX -= crop.X;
                arrow.StartY -= crop.Y;
                arrow.EndX -= crop.X;
                arrow.EndY -= crop.Y;
                return true;
            }
            case TextAnnotation text:
            {
                if (!crop.Contains(text.X, text.Y))
                {
                    return false;
                }

                text.X -= crop.X;
                text.Y -= crop.Y;
                return true;
            }
            case RectangleAnnotation rect:
            {
                var x = rect.X;
                var y = rect.Y;
                var width = rect.Width;
                var height = rect.Height;
                if (!TryClipRect(ref x, ref y, ref width, ref height, crop))
                {
                    return false;
                }

                rect.X = x;
                rect.Y = y;
                rect.Width = width;
                rect.Height = height;
                return true;
            }
            case HighlightAnnotation highlight:
            {
                var x = highlight.X;
                var y = highlight.Y;
                var width = highlight.Width;
                var height = highlight.Height;
                if (!TryClipRect(ref x, ref y, ref width, ref height, crop))
                {
                    return false;
                }

                highlight.X = x;
                highlight.Y = y;
                highlight.Width = width;
                highlight.Height = height;
                return true;
            }
            case BlurAnnotation blur:
            {
                var x = blur.X;
                var y = blur.Y;
                var width = blur.Width;
                var height = blur.Height;
                if (!TryClipRect(ref x, ref y, ref width, ref height, crop))
                {
                    return false;
                }

                blur.X = x;
                blur.Y = y;
                blur.Width = width;
                blur.Height = height;
                return true;
            }
            case PenAnnotation pen:
            {
                if (pen.Points.Count == 0 || !pen.Points.Any(point => crop.Contains(point.X, point.Y)))
                {
                    return false;
                }

                for (var index = 0; index < pen.Points.Count; index++)
                {
                    var point = pen.Points[index];
                    pen.Points[index] = new PointD(point.X - crop.X, point.Y - crop.Y);
                }

                return true;
            }
            default:
                return false;
        }
    }

    private static bool TryClipRect(ref double x, ref double y, ref double width, ref double height, RectD crop)
    {
        var bounds = NormalizeRect(x, y, width, height);
        var left = Math.Max(bounds.X, crop.X);
        var top = Math.Max(bounds.Y, crop.Y);
        var right = Math.Min(bounds.X + bounds.Width, crop.X + crop.Width);
        var bottom = Math.Min(bounds.Y + bounds.Height, crop.Y + crop.Height);
        if (right - left < 1 || bottom - top < 1)
        {
            return false;
        }

        x = left - crop.X;
        y = top - crop.Y;
        width = right - left;
        height = bottom - top;
        return true;
    }

    private static bool SegmentIntersectsRect(double x1, double y1, double x2, double y2, RectD rect)
    {
        if (rect.Contains(x1, y1) || rect.Contains(x2, y2))
        {
            return true;
        }

        return LineIntersects(x1, y1, x2, y2, rect.X, rect.Y, rect.X + rect.Width, rect.Y)
               || LineIntersects(x1, y1, x2, y2, rect.X + rect.Width, rect.Y, rect.X + rect.Width, rect.Y + rect.Height)
               || LineIntersects(x1, y1, x2, y2, rect.X, rect.Y + rect.Height, rect.X + rect.Width, rect.Y + rect.Height)
               || LineIntersects(x1, y1, x2, y2, rect.X, rect.Y, rect.X, rect.Y + rect.Height);
    }

    private static bool LineIntersects(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
    {
        var denominator = ((x1 - x2) * (y3 - y4)) - ((y1 - y2) * (x3 - x4));
        if (Math.Abs(denominator) < 0.0001)
        {
            return false;
        }

        var t = (((x1 - x3) * (y3 - y4)) - ((y1 - y3) * (x3 - x4))) / denominator;
        var u = -(((x1 - x2) * (y1 - y3)) - ((y1 - y2) * (x1 - x3))) / denominator;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1;
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

    private static bool HitTestPen(PenAnnotation pen, double x, double y)
    {
        for (var index = 1; index < pen.Points.Count; index++)
        {
            var previous = pen.Points[index - 1];
            var current = pen.Points[index];
            if (DistanceToSegment(x, y, previous.X, previous.Y, current.X, current.Y) <= HitTolerance + pen.StrokeWidth)
            {
                return true;
            }
        }

        return pen.Points.Count == 1
               && DistanceToSegment(x, y, pen.Points[0].X, pen.Points[0].Y, pen.Points[0].X, pen.Points[0].Y) <= HitTolerance + pen.StrokeWidth;
    }

    private static RectD GetPenBounds(PenAnnotation pen)
    {
        if (pen.Points.Count == 0)
        {
            return RectD.Empty;
        }

        var minX = pen.Points.Min(static point => point.X);
        var minY = pen.Points.Min(static point => point.Y);
        var maxX = pen.Points.Max(static point => point.X);
        var maxY = pen.Points.Max(static point => point.Y);
        return new RectD(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
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

    private static RectD EstimateTextBounds(TextAnnotation text)
    {
        var lines = text.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var maxLineLength = lines.Length == 0 ? 0 : lines.Max(static line => line.Length);
        var width = Math.Max(24, maxLineLength * text.FontSize * 0.55);
        var height = Math.Max(text.FontSize * 1.4, lines.Length * text.FontSize * 1.25);
        return new RectD(text.X, text.Y - text.FontSize, width, height);
    }
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public static RectD Empty { get; } = new(0, 0, 0, 0);

    public bool Contains(double x, double y)
        => x >= X && y >= Y && x <= X + Width && y <= Y + Height;

    public bool Intersects(RectD other)
    {
        return X < other.X + other.Width
               && X + Width > other.X
               && Y < other.Y + other.Height
               && Y + Height > other.Y;
    }
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
