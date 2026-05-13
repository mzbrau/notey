using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace Notey.App.Editing;

public sealed class ImagePreviewMargin : AbstractMargin
{
    private static readonly IBrush GlyphBackgroundBrush = Brush.Parse("#18253E");
    private static readonly IBrush GlyphStrokeBrush = Brush.Parse("#ADC6FF");
    private static readonly IBrush GlyphAccentBrush = Brush.Parse("#E1E2EC");
    private static readonly Pen GlyphBorderPen = new(GlyphStrokeBrush, 1);
    private static readonly Pen GlyphLinePen = new(GlyphAccentBrush, 1.1);

    private readonly List<HitTarget> _hitTargets = [];
    private IReadOnlyDictionary<int, ObsidianImageEmbed> _embeds = new Dictionary<int, ObsidianImageEmbed>();

    public ImagePreviewMargin()
    {
        Width = 22;
        ClipToBounds = true;
    }

    public event EventHandler<ImagePreviewRequestedEventArgs>? PreviewRequested;

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        base.OnTextViewChanged(oldTextView, newTextView);

        if (oldTextView is not null)
        {
            oldTextView.ScrollOffsetChanged -= OnScrollOffsetChanged;
        }

        if (newTextView is not null)
        {
            newTextView.ScrollOffsetChanged += OnScrollOffsetChanged;
        }

        InvalidateVisual();
    }

    protected override void OnDocumentChanged(TextDocument oldDocument, TextDocument newDocument)
    {
        base.OnDocumentChanged(oldDocument, newDocument);

        if (oldDocument is not null)
        {
            oldDocument.TextChanged -= OnDocumentTextChanged;
        }

        if (newDocument is not null)
        {
            newDocument.TextChanged += OnDocumentTextChanged;
        }

        RebuildEmbeds();
    }

    protected override void OnTextViewVisualLinesChanged()
    {
        base.OnTextViewVisualLinesChanged();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(Width, 0);
    }

    public override void Render(DrawingContext drawingContext)
    {
        base.Render(drawingContext);
        _hitTargets.Clear();

        if (TextView is null || Document is null || !TextView.VisualLinesValid)
        {
            return;
        }

        foreach (var visualLine in TextView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (!_embeds.TryGetValue(lineNumber, out var embed))
            {
                continue;
            }

            var glyphBounds = CreateGlyphBounds(visualLine);
            DrawGlyph(drawingContext, glyphBounds);
            _hitTargets.Add(new HitTarget(glyphBounds, embed));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = TryFindHitTarget(e.GetPosition(this)) is null
            ? new Cursor(StandardCursorType.Arrow)
            : new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        var target = TryFindHitTarget(point);
        if (target is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        PreviewRequested?.Invoke(this, new ImagePreviewRequestedEventArgs(target.Embed));
        e.Handled = true;
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        RebuildEmbeds();
    }

    private void OnScrollOffsetChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private void RebuildEmbeds()
    {
        _embeds = Document is null
            ? new Dictionary<int, ObsidianImageEmbed>()
            : ObsidianImageEmbedIndex.Build(Document.Text);
        InvalidateVisual();
    }

    private Rect CreateGlyphBounds(VisualLine visualLine)
    {
        var glyphSize = Math.Clamp(visualLine.Height - 6, 10, 14);
        var top = visualLine.VisualTop - TextView!.VerticalOffset + Math.Max(0, (visualLine.Height - glyphSize) / 2);
        var left = Math.Max(2, (Bounds.Width - glyphSize) / 2);
        return new Rect(left, top, glyphSize, glyphSize);
    }

    private static void DrawGlyph(DrawingContext drawingContext, Rect glyphBounds)
    {
        drawingContext.DrawRectangle(GlyphBackgroundBrush, GlyphBorderPen, glyphBounds, 3, 3);

        var dotCenter = new Point(glyphBounds.X + (glyphBounds.Width * 0.32), glyphBounds.Y + (glyphBounds.Height * 0.34));
        drawingContext.DrawEllipse(GlyphAccentBrush, null, dotCenter, 1.2, 1.2);

        drawingContext.DrawLine(
            GlyphLinePen,
            new Point(glyphBounds.X + 2.2, glyphBounds.Bottom - 2.4),
            new Point(glyphBounds.X + (glyphBounds.Width * 0.46), glyphBounds.Y + (glyphBounds.Height * 0.56)));
        drawingContext.DrawLine(
            GlyphLinePen,
            new Point(glyphBounds.X + (glyphBounds.Width * 0.46), glyphBounds.Y + (glyphBounds.Height * 0.56)),
            new Point(glyphBounds.X + (glyphBounds.Width * 0.66), glyphBounds.Y + (glyphBounds.Height * 0.74)));
        drawingContext.DrawLine(
            GlyphLinePen,
            new Point(glyphBounds.X + (glyphBounds.Width * 0.66), glyphBounds.Y + (glyphBounds.Height * 0.74)),
            new Point(glyphBounds.Right - 2.2, glyphBounds.Y + (glyphBounds.Height * 0.42)));
    }

    private HitTarget? TryFindHitTarget(Point point)
    {
        return _hitTargets.FirstOrDefault(target => target.Bounds.Contains(point));
    }

    private sealed record HitTarget(Rect Bounds, ObsidianImageEmbed Embed);
}

public sealed class ImagePreviewRequestedEventArgs(ObsidianImageEmbed embed) : EventArgs
{
    public ObsidianImageEmbed Embed { get; } = embed;
}
