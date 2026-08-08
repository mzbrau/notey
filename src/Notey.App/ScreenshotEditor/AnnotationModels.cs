namespace Notey.App.ScreenshotEditor;

public enum AnnotationTool
{
    Select,
    Arrow,
    Text,
    Rectangle,
    Highlight,
    Blur,
    Crop
}

public abstract class Annotation
{
    public Guid Id { get; } = Guid.NewGuid();

    public uint ColorArgb { get; set; } = 0xFFE53935;

    public abstract Annotation Clone();
}

public sealed class ArrowAnnotation : Annotation
{
    public double StartX { get; set; }

    public double StartY { get; set; }

    public double EndX { get; set; }

    public double EndY { get; set; }

    public double StrokeWidth { get; set; } = 3;

    public override Annotation Clone() => new ArrowAnnotation
    {
        ColorArgb = ColorArgb,
        StartX = StartX,
        StartY = StartY,
        EndX = EndX,
        EndY = EndY,
        StrokeWidth = StrokeWidth
    };
}

public sealed class TextAnnotation : Annotation
{
    public double X { get; set; }

    public double Y { get; set; }

    public string Text { get; set; } = "Text";

    public double FontSize { get; set; } = 24;

    public override Annotation Clone() => new TextAnnotation
    {
        ColorArgb = ColorArgb,
        X = X,
        Y = Y,
        Text = Text,
        FontSize = FontSize
    };
}

public sealed class RectangleAnnotation : Annotation
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double StrokeWidth { get; set; } = 3;

    public override Annotation Clone() => new RectangleAnnotation
    {
        ColorArgb = ColorArgb,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        StrokeWidth = StrokeWidth
    };
}

public sealed class HighlightAnnotation : Annotation
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public byte Opacity { get; set; } = 96;

    public override Annotation Clone() => new HighlightAnnotation
    {
        ColorArgb = ColorArgb,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Opacity = Opacity
    };
}

public sealed class BlurAnnotation : Annotation
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public int PixelSize { get; set; } = 12;

    public override Annotation Clone() => new BlurAnnotation
    {
        ColorArgb = ColorArgb,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        PixelSize = PixelSize
    };
}

public sealed class ScreenshotEditDocument
{
    public ScreenshotEditDocument(byte[] pngBytes, int width, int height)
    {
        PngBytes = pngBytes;
        Width = width;
        Height = height;
    }

    public byte[] PngBytes { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public List<Annotation> Annotations { get; } = [];

    public Annotation? SelectedAnnotation { get; set; }

    public uint CurrentColorArgb { get; set; } = 0xFFE53935;

    public AnnotationTool CurrentTool { get; set; } = AnnotationTool.Select;

    public void ReplaceBaseImage(byte[] pngBytes, int width, int height)
    {
        PngBytes = pngBytes;
        Width = width;
        Height = height;
        Annotations.Clear();
        SelectedAnnotation = null;
    }
}
