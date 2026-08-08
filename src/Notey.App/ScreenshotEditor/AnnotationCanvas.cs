using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Notey.App.ScreenshotEditor;

public sealed class AnnotationCanvas : Control
{
    private readonly ScreenshotEditDocument _document;
    private Bitmap? _baseBitmap;
    private Point? _dragOrigin;
    private Annotation? _draftAnnotation;
    private HandleKind? _activeHandle;
    private bool _isMovingSelection;
    private RectD? _cropPreview;
    private bool _awaitingTextCommit;

    public AnnotationCanvas(ScreenshotEditDocument document)
    {
        _document = document;
        Focusable = true;
        ClipToBounds = true;
        ReloadBaseBitmap();
        Width = document.Width;
        Height = document.Height;
    }

    public event EventHandler? DocumentChanged;

    public void ReloadBaseBitmap()
    {
        _baseBitmap?.Dispose();
        using var stream = new MemoryStream(_document.PngBytes);
        _baseBitmap = new Bitmap(stream);
        Width = _document.Width;
        Height = _document.Height;
        InvalidateVisual();
    }

    public void NotifyExternalChange()
    {
        InvalidateVisual();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSelected()
    {
        if (_document.SelectedAnnotation is not { } selected)
        {
            return;
        }

        _document.Annotations.Remove(selected);
        _document.SelectedAnnotation = null;
        InvalidateVisual();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        _dragOrigin = point;
        e.Pointer.Capture(this);
        e.Handled = true;

        if (_document.CurrentTool == AnnotationTool.Select)
        {
            if (_document.SelectedAnnotation is { } selected)
            {
                var handle = AnnotationGeometry.HitTestHandle(selected, point.X, point.Y);
                if (handle is not null)
                {
                    _activeHandle = handle;
                    return;
                }

                if (AnnotationGeometry.HitTest(selected, point.X, point.Y))
                {
                    _isMovingSelection = true;
                    return;
                }
            }

            for (var index = _document.Annotations.Count - 1; index >= 0; index--)
            {
                var candidate = _document.Annotations[index];
                if (!AnnotationGeometry.HitTest(candidate, point.X, point.Y))
                {
                    continue;
                }

                _document.SelectedAnnotation = candidate;
                _isMovingSelection = true;
                InvalidateVisual();
                return;
            }

            _document.SelectedAnnotation = null;
            InvalidateVisual();
            return;
        }

        if (_document.CurrentTool == AnnotationTool.Crop)
        {
            _cropPreview = new RectD(point.X, point.Y, 0, 0);
            InvalidateVisual();
            return;
        }

        _draftAnnotation = CreateDraft(point);
        if (_draftAnnotation is not null)
        {
            _document.Annotations.Add(_draftAnnotation);
            _document.SelectedAnnotation = _draftAnnotation;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragOrigin is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var origin = _dragOrigin.Value;

        if (_document.CurrentTool == AnnotationTool.Crop && _cropPreview is not null)
        {
            _cropPreview = AnnotationGeometry.NormalizeRect(origin.X, origin.Y, point.X - origin.X, point.Y - origin.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_activeHandle is { } handle && _document.SelectedAnnotation is { } resizing)
        {
            AnnotationGeometry.ApplyHandleDrag(resizing, handle, point.X, point.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isMovingSelection && _document.SelectedAnnotation is { } moving)
        {
            AnnotationGeometry.Move(moving, point.X - origin.X, point.Y - origin.Y);
            _dragOrigin = point;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_draftAnnotation is not null)
        {
            UpdateDraft(_draftAnnotation, origin, point);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override async void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        e.Handled = true;

        if (_document.CurrentTool == AnnotationTool.Crop && _cropPreview is { } crop && crop.Width >= 4 && crop.Height >= 4)
        {
            var (png, width, height) = AnnotationCompositor.Crop(_document, crop);
            _document.ReplaceBaseImage(png, width, height);
            ReloadBaseBitmap();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_draftAnnotation is TextAnnotation text && !_awaitingTextCommit)
        {
            _awaitingTextCommit = true;
            try
            {
                var edited = await PromptForTextAsync(text.Text);
                if (string.IsNullOrWhiteSpace(edited))
                {
                    _document.Annotations.Remove(text);
                    _document.SelectedAnnotation = null;
                }
                else
                {
                    text.Text = edited.Trim();
                }
            }
            finally
            {
                _awaitingTextCommit = false;
            }
        }

        _dragOrigin = null;
        _draftAnnotation = null;
        _activeHandle = null;
        _isMovingSelection = false;
        _cropPreview = null;
        InvalidateVisual();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_baseBitmap is not null)
        {
            context.DrawImage(_baseBitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }

        foreach (var annotation in _document.Annotations)
        {
            DrawAnnotation(context, annotation, selected: ReferenceEquals(annotation, _document.SelectedAnnotation));
        }

        if (_cropPreview is { } crop)
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                new Pen(Brushes.White, 1),
                new Rect(crop.X, crop.Y, crop.Width, crop.Height));
        }
    }

    private Annotation? CreateDraft(Point point)
    {
        return _document.CurrentTool switch
        {
            AnnotationTool.Arrow => new ArrowAnnotation
            {
                ColorArgb = _document.CurrentColorArgb,
                StartX = point.X,
                StartY = point.Y,
                EndX = point.X,
                EndY = point.Y
            },
            AnnotationTool.Text => new TextAnnotation
            {
                ColorArgb = _document.CurrentColorArgb,
                X = point.X,
                Y = point.Y,
                Text = "Text"
            },
            AnnotationTool.Rectangle => new RectangleAnnotation
            {
                ColorArgb = _document.CurrentColorArgb,
                X = point.X,
                Y = point.Y
            },
            AnnotationTool.Highlight => new HighlightAnnotation
            {
                ColorArgb = _document.CurrentColorArgb,
                X = point.X,
                Y = point.Y
            },
            AnnotationTool.Blur => new BlurAnnotation
            {
                X = point.X,
                Y = point.Y
            },
            _ => null
        };
    }

    private static void UpdateDraft(Annotation annotation, Point origin, Point current)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow:
                arrow.EndX = current.X;
                arrow.EndY = current.Y;
                break;
            case RectangleAnnotation rect:
                rect.X = Math.Min(origin.X, current.X);
                rect.Y = Math.Min(origin.Y, current.Y);
                rect.Width = Math.Abs(current.X - origin.X);
                rect.Height = Math.Abs(current.Y - origin.Y);
                break;
            case HighlightAnnotation highlight:
                highlight.X = Math.Min(origin.X, current.X);
                highlight.Y = Math.Min(origin.Y, current.Y);
                highlight.Width = Math.Abs(current.X - origin.X);
                highlight.Height = Math.Abs(current.Y - origin.Y);
                break;
            case BlurAnnotation blur:
                blur.X = Math.Min(origin.X, current.X);
                blur.Y = Math.Min(origin.Y, current.Y);
                blur.Width = Math.Abs(current.X - origin.X);
                blur.Height = Math.Abs(current.Y - origin.Y);
                break;
        }
    }

    private static void DrawAnnotation(DrawingContext context, Annotation annotation, bool selected)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow:
                var color = Color.FromUInt32(arrow.ColorArgb);
                var pen = new Pen(new SolidColorBrush(color), arrow.StrokeWidth);
                context.DrawLine(pen, new Point(arrow.StartX, arrow.StartY), new Point(arrow.EndX, arrow.EndY));
                DrawArrowHead(context, pen, arrow);
                break;
            case TextAnnotation text:
                var textColor = Color.FromUInt32(text.ColorArgb);
                var formatted = new FormattedText(
                    text.Text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    text.FontSize,
                    new SolidColorBrush(textColor));
                context.DrawText(formatted, new Point(text.X, text.Y - text.FontSize));
                break;
            case RectangleAnnotation rect:
                var rectBounds = AnnotationGeometry.NormalizeRect(rect.X, rect.Y, rect.Width, rect.Height);
                context.DrawRectangle(
                    null,
                    new Pen(new SolidColorBrush(Color.FromUInt32(rect.ColorArgb)), rect.StrokeWidth),
                    new Rect(rectBounds.X, rectBounds.Y, rectBounds.Width, rectBounds.Height));
                break;
            case HighlightAnnotation highlight:
                var highlightBounds = AnnotationGeometry.NormalizeRect(highlight.X, highlight.Y, highlight.Width, highlight.Height);
                var fill = Color.FromUInt32(highlight.ColorArgb);
                fill = Color.FromArgb(highlight.Opacity, fill.R, fill.G, fill.B);
                context.FillRectangle(
                    new SolidColorBrush(fill),
                    new Rect(highlightBounds.X, highlightBounds.Y, highlightBounds.Width, highlightBounds.Height));
                break;
            case BlurAnnotation blur:
                var blurBounds = AnnotationGeometry.NormalizeRect(blur.X, blur.Y, blur.Width, blur.Height);
                context.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(64, 80, 80, 80)),
                    new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1),
                    new Rect(blurBounds.X, blurBounds.Y, blurBounds.Width, blurBounds.Height));
                break;
        }

        if (selected)
        {
            foreach (var handle in AnnotationGeometry.GetHandles(annotation))
            {
                context.DrawRectangle(
                    Brushes.White,
                    new Pen(Brushes.Black, 1),
                    new Rect(
                        handle.X - AnnotationGeometry.HandleSize / 2,
                        handle.Y - AnnotationGeometry.HandleSize / 2,
                        AnnotationGeometry.HandleSize,
                        AnnotationGeometry.HandleSize));
            }
        }
    }

    private static void DrawArrowHead(DrawingContext context, IPen pen, ArrowAnnotation arrow)
    {
        var angle = Math.Atan2(arrow.EndY - arrow.StartY, arrow.EndX - arrow.StartX);
        var headLength = 14 + arrow.StrokeWidth * 2;
        var left = new Point(
            arrow.EndX - headLength * Math.Cos(angle - Math.PI / 6),
            arrow.EndY - headLength * Math.Sin(angle - Math.PI / 6));
        var right = new Point(
            arrow.EndX - headLength * Math.Cos(angle + Math.PI / 6),
            arrow.EndY - headLength * Math.Sin(angle + Math.PI / 6));
        context.DrawLine(pen, new Point(arrow.EndX, arrow.EndY), left);
        context.DrawLine(pen, new Point(arrow.EndX, arrow.EndY), right);
    }

    private async Task<string?> PromptForTextAsync(string initial)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return initial;
        }

        var input = new TextBox
        {
            Text = initial,
            MinWidth = 280,
            PlaceholderText = "Annotation text"
        };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 72 };
        var okButton = new Button { Content = "OK", MinWidth = 72 };
        var dialog = new Window
        {
            Title = "Edit text",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    input,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close(null);
        okButton.Click += (_, _) => dialog.Close(input.Text);
        return await dialog.ShowDialog<string?>(owner);
    }
}
