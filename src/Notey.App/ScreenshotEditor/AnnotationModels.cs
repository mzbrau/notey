namespace Notey.App.ScreenshotEditor;

public enum AnnotationTool
{
    Select,
    Arrow,
    Text,
    Rectangle,
    Highlight,
    Blur,
    Pen,
    Eyedropper,
    PaintBucket,
    Crop
}

public abstract class Annotation
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public uint ColorArgb { get; set; } = 0xFFE53935;

    public abstract Annotation Clone();

    protected T CloneCore<T>(T clone) where T : Annotation
    {
        clone.Id = Id;
        clone.ColorArgb = ColorArgb;
        return clone;
    }
}

public interface IStrokeAnnotation
{
    double StrokeWidth { get; set; }
}

public sealed class ArrowAnnotation : Annotation, IStrokeAnnotation
{
    public double StartX { get; set; }

    public double StartY { get; set; }

    public double EndX { get; set; }

    public double EndY { get; set; }

    public double StrokeWidth { get; set; } = 3;

    public override Annotation Clone() => CloneCore(new ArrowAnnotation
    {
        StartX = StartX,
        StartY = StartY,
        EndX = EndX,
        EndY = EndY,
        StrokeWidth = StrokeWidth
    });
}

public sealed class TextAnnotation : Annotation
{
    public double X { get; set; }

    public double Y { get; set; }

    public string Text { get; set; } = "Text";

    public double FontSize { get; set; } = 24;

    public bool IsBold { get; set; }

    public bool IsItalic { get; set; }

    /// <summary>
    /// Optional backdrop behind the text. Null means no background.
    /// </summary>
    public uint? BackgroundColorArgb { get; set; }

    public override Annotation Clone() => CloneCore(new TextAnnotation
    {
        X = X,
        Y = Y,
        Text = Text,
        FontSize = FontSize,
        IsBold = IsBold,
        IsItalic = IsItalic,
        BackgroundColorArgb = BackgroundColorArgb
    });
}

public sealed class RectangleAnnotation : Annotation, IStrokeAnnotation
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double StrokeWidth { get; set; } = 3;

    public override Annotation Clone() => CloneCore(new RectangleAnnotation
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        StrokeWidth = StrokeWidth
    });
}

public sealed class HighlightAnnotation : Annotation
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public byte Opacity { get; set; } = 96;

    public override Annotation Clone() => CloneCore(new HighlightAnnotation
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Opacity = Opacity
    });
}

public sealed class BlurAnnotation : Annotation
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public int PixelSize { get; set; } = 12;

    public override Annotation Clone() => CloneCore(new BlurAnnotation
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        PixelSize = PixelSize
    });
}

public sealed class PenAnnotation : Annotation, IStrokeAnnotation
{
    public List<PointD> Points { get; } = [];

    public double StrokeWidth { get; set; } = 3;

    public override Annotation Clone()
    {
        var clone = CloneCore(new PenAnnotation { StrokeWidth = StrokeWidth });
        clone.Points.AddRange(Points);
        return clone;
    }
}

public readonly record struct PointD(double X, double Y);

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

    public double CurrentStrokeWidth { get; set; } = 3;

    public int CurrentFillTolerance { get; set; } = 32;

    public AnnotationTool CurrentTool { get; set; } = AnnotationTool.Select;

    public void ReplaceBaseImage(byte[] pngBytes, int width, int height)
    {
        PngBytes = pngBytes;
        Width = width;
        Height = height;
    }

    public void ApplyCrop(RectD crop)
    {
        var clamped = AnnotationGeometry.ClampCrop(crop, Width, Height);
        var (png, width, height) = AnnotationCompositor.CropBaseImage(PngBytes, clamped);
        var kept = AnnotationGeometry.TransformAnnotationsForCrop(Annotations, clamped);
        ReplaceBaseImage(png, width, height);
        Annotations.Clear();
        Annotations.AddRange(kept);
        SelectedAnnotation = null;
    }

    public void RestoreSnapshot(EditSnapshot snapshot)
    {
        PngBytes = snapshot.PngBytes;
        Width = snapshot.Width;
        Height = snapshot.Height;
        Annotations.Clear();
        foreach (var annotation in snapshot.Annotations)
        {
            Annotations.Add(annotation.Clone());
        }

        SelectedAnnotation = snapshot.SelectedAnnotationId is { } selectedId
            ? Annotations.FirstOrDefault(annotation => annotation.Id == selectedId)
            : null;
        CurrentColorArgb = snapshot.CurrentColorArgb;
        CurrentStrokeWidth = snapshot.CurrentStrokeWidth;
    }

    public EditSnapshot CreateSnapshot()
    {
        return new EditSnapshot(
            PngBytes.ToArray(),
            Width,
            Height,
            Annotations.Select(static annotation => annotation.Clone()).ToList(),
            SelectedAnnotation?.Id,
            CurrentColorArgb,
            CurrentStrokeWidth);
    }
}

public sealed record EditSnapshot(
    byte[] PngBytes,
    int Width,
    int Height,
    IReadOnlyList<Annotation> Annotations,
    Guid? SelectedAnnotationId,
    uint CurrentColorArgb,
    double CurrentStrokeWidth);
