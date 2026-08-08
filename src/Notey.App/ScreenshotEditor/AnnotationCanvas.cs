using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Notey.App.ScreenshotEditor;

public sealed class AnnotationCanvas : Canvas
{
    private readonly ScreenshotEditDocument _document;
    private readonly ScreenshotEditHistory _history;
    private readonly Surface _surface;
    private TextBox? _textEditor;
    private TextAnnotation? _editingText;
    private bool _suppressTextCommit;

    public AnnotationCanvas(ScreenshotEditDocument document, ScreenshotEditHistory history)
    {
        _document = document;
        _history = history;
        _surface = new Surface(this);
        SetLeft(_surface, 0);
        SetTop(_surface, 0);
        Children.Add(_surface);
        Width = document.Width;
        Height = document.Height;
        ClipToBounds = true;
    }

    public event EventHandler? DocumentChanged;

    public event EventHandler? ToolReturnedToSelect;

    public event EventHandler? AppearanceChanged;

    public void ReloadBaseBitmap()
    {
        Width = _document.Width;
        Height = _document.Height;
        _surface.ReloadBaseBitmap();
        InvalidateVisual();
    }

    public void NotifyExternalChange()
    {
        _surface.InvalidateVisual();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSelected()
    {
        if (_document.SelectedAnnotation is not { } selected)
        {
            return;
        }

        _history.Push(_document);
        _document.Annotations.Remove(selected);
        _document.SelectedAnnotation = null;
        CloseTextEditor(commit: false);
        NotifyExternalChange();
    }

    public bool Undo()
    {
        CloseTextEditor(commit: false);
        if (!_history.TryUndo(_document))
        {
            return false;
        }

        ReloadBaseBitmap();
        NotifyExternalChange();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void BeginEditSelectedText()
    {
        if (_document.SelectedAnnotation is TextAnnotation text)
        {
            OpenTextEditor(text);
        }
    }

    private void ReturnToSelect()
    {
        _document.CurrentTool = AnnotationTool.Select;
        ToolReturnedToSelect?.Invoke(this, EventArgs.Empty);
    }

    private void OpenTextEditor(TextAnnotation text)
    {
        CloseTextEditor(commit: false);
        _editingText = text;
        _textEditor = new TextBox
        {
            Text = text.Text == "Text" ? string.Empty : text.Text,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.Wrap,
            Width = Math.Max(160, AnnotationGeometry.GetBounds(text).Width + 24),
            MinHeight = Math.Max(32, text.FontSize + 12),
            FontSize = text.FontSize,
            FontWeight = text.IsBold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = text.IsItalic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = new SolidColorBrush(Color.FromUInt32(text.ColorArgb)),
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 24, 32)),
            BorderBrush = Brush.Parse("#ADC6FF"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4)
        };

        SetLeft(_textEditor, text.X);
        SetTop(_textEditor, text.Y - text.FontSize);
        Children.Add(_textEditor);
        _textEditor.LostFocus += OnTextEditorLostFocus;
        _textEditor.KeyDown += OnTextEditorKeyDown;
        Dispatcher.UIThread.Post(() => _textEditor?.Focus());
    }

    private void OnTextEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseTextEditor(commit: false, removeIfEmpty: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _textEditor is not null)
        {
            var caret = _textEditor.CaretIndex;
            var value = _textEditor.Text ?? string.Empty;
            _textEditor.Text = value.Insert(Math.Clamp(caret, 0, value.Length), Environment.NewLine);
            _textEditor.CaretIndex = caret + Environment.NewLine.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            CloseTextEditor(commit: true);
            e.Handled = true;
        }
    }

    private void OnTextEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressTextCommit)
        {
            return;
        }

        CloseTextEditor(commit: true);
    }

    private void CloseTextEditor(bool commit, bool removeIfEmpty = false)
    {
        if (_textEditor is null || _editingText is null)
        {
            return;
        }

        _suppressTextCommit = true;
        try
        {
            _textEditor.LostFocus -= OnTextEditorLostFocus;
            _textEditor.KeyDown -= OnTextEditorKeyDown;
            var text = _textEditor.Text?.TrimEnd('\r', '\n') ?? string.Empty;
            Children.Remove(_textEditor);
            _textEditor = null;

            if (commit)
            {
                if (string.IsNullOrWhiteSpace(text) || removeIfEmpty && string.IsNullOrWhiteSpace(text))
                {
                    _document.Annotations.Remove(_editingText);
                    if (ReferenceEquals(_document.SelectedAnnotation, _editingText))
                    {
                        _document.SelectedAnnotation = null;
                    }
                }
                else if (!string.Equals(_editingText.Text, text, StringComparison.Ordinal))
                {
                    _history.Push(_document);
                    _editingText.Text = text;
                }
                else
                {
                    _editingText.Text = text;
                }
            }
            else if (removeIfEmpty || _editingText.Text == "Text")
            {
                _document.Annotations.Remove(_editingText);
                if (ReferenceEquals(_document.SelectedAnnotation, _editingText))
                {
                    _document.SelectedAnnotation = null;
                }
            }

            _editingText = null;
            NotifyExternalChange();
            ReturnToSelect();
        }
        finally
        {
            _suppressTextCommit = false;
        }
    }

    private sealed class Surface : Control
    {
        private readonly AnnotationCanvas _owner;
        private Bitmap? _baseBitmap;
        private Point? _dragOrigin;
        private Annotation? _draftAnnotation;
        private HandleKind? _activeHandle;
        private bool _isMovingSelection;
        private RectD? _cropPreview;
        private bool _mutationPushed;
        private readonly Dictionary<Guid, Bitmap> _blurPreviewCache = [];

        public Surface(AnnotationCanvas owner)
        {
            _owner = owner;
            Focusable = true;
            ClipToBounds = true;
            ReloadBaseBitmap();
            Width = owner._document.Width;
            Height = owner._document.Height;
        }

        public void ReloadBaseBitmap()
        {
            _baseBitmap?.Dispose();
            foreach (var preview in _blurPreviewCache.Values)
            {
                preview.Dispose();
            }

            _blurPreviewCache.Clear();
            using var stream = new MemoryStream(_owner._document.PngBytes);
            _baseBitmap = new Bitmap(stream);
            Width = _owner._document.Width;
            Height = _owner._document.Height;
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Delete)
            {
                _owner.DeleteSelected();
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

            var document = _owner._document;
            var point = e.GetPosition(this);
            _dragOrigin = point;
            _mutationPushed = false;
            e.Pointer.Capture(this);
            e.Handled = true;

            if (document.CurrentTool == AnnotationTool.Eyedropper)
            {
                _owner._history.Push(document);
                var color = ImagePixelOps.SamplePngPixel(
                    document.PngBytes,
                    (int)Math.Clamp(point.X, 0, document.Width - 1),
                    (int)Math.Clamp(point.Y, 0, document.Height - 1));
                document.CurrentColorArgb = color;
                if (document.SelectedAnnotation is { } selected)
                {
                    selected.ColorArgb = color;
                }

                _owner.AppearanceChanged?.Invoke(_owner, EventArgs.Empty);
                _owner.NotifyExternalChange();
                _owner.ReturnToSelect();
                return;
            }

            if (document.CurrentTool == AnnotationTool.PaintBucket)
            {
                _owner._history.Push(document);
                var filled = ImagePixelOps.FloodFillPng(
                    document.PngBytes,
                    (int)Math.Clamp(point.X, 0, document.Width - 1),
                    (int)Math.Clamp(point.Y, 0, document.Height - 1),
                    document.CurrentColorArgb);
                document.ReplaceBaseImage(filled, document.Width, document.Height);
                ReloadBaseBitmap();
                _owner.NotifyExternalChange();
                _owner.ReturnToSelect();
                return;
            }

            if (document.CurrentTool == AnnotationTool.Select)
            {
                if (document.SelectedAnnotation is { } selected)
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

                for (var index = document.Annotations.Count - 1; index >= 0; index--)
                {
                    var candidate = document.Annotations[index];
                    if (!AnnotationGeometry.HitTest(candidate, point.X, point.Y))
                    {
                        continue;
                    }

                    document.SelectedAnnotation = candidate;
                    if (candidate is IStrokeAnnotation stroke)
                    {
                        document.CurrentStrokeWidth = stroke.StrokeWidth;
                    }

                    document.CurrentColorArgb = candidate.ColorArgb;
                    _owner.AppearanceChanged?.Invoke(_owner, EventArgs.Empty);

                    if (e.ClickCount >= 2 && candidate is TextAnnotation textAnnotation)
                    {
                        _owner.OpenTextEditor(textAnnotation);
                        InvalidateVisual();
                        return;
                    }

                    _isMovingSelection = true;
                    InvalidateVisual();
                    return;
                }

                document.SelectedAnnotation = null;
                InvalidateVisual();
                return;
            }

            if (document.CurrentTool == AnnotationTool.Crop)
            {
                _cropPreview = new RectD(point.X, point.Y, 0, 0);
                InvalidateVisual();
                return;
            }

            if (document.CurrentTool == AnnotationTool.Text)
            {
                _owner._history.Push(document);
                var text = new TextAnnotation
                {
                    ColorArgb = document.CurrentColorArgb,
                    X = point.X,
                    Y = point.Y,
                    Text = "Text",
                    FontSize = 24
                };
                document.Annotations.Add(text);
                document.SelectedAnnotation = text;
                InvalidateVisual();
                _owner.OpenTextEditor(text);
                return;
            }

            _draftAnnotation = CreateDraft(point);
            if (_draftAnnotation is not null)
            {
                _owner._history.Push(document);
                _mutationPushed = true;
                document.Annotations.Add(_draftAnnotation);
                document.SelectedAnnotation = _draftAnnotation;
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

            var document = _owner._document;
            var point = e.GetPosition(this);
            var origin = _dragOrigin.Value;

            if (document.CurrentTool == AnnotationTool.Crop && _cropPreview is not null)
            {
                _cropPreview = AnnotationGeometry.NormalizeRect(origin.X, origin.Y, point.X - origin.X, point.Y - origin.Y);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_activeHandle is { } handle && document.SelectedAnnotation is { } resizing)
            {
                EnsureMutationPushed();
                AnnotationGeometry.ApplyHandleDrag(resizing, handle, point.X, point.Y);
                if (resizing is BlurAnnotation blurResize)
                {
                    InvalidateBlurPreview(blurResize);
                }

                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_isMovingSelection && document.SelectedAnnotation is { } moving)
            {
                EnsureMutationPushed();
                AnnotationGeometry.Move(moving, point.X - origin.X, point.Y - origin.Y);
                if (moving is BlurAnnotation blurMove)
                {
                    InvalidateBlurPreview(blurMove);
                }

                _dragOrigin = point;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_draftAnnotation is PenAnnotation pen)
            {
                var last = pen.Points[^1];
                if (Math.Abs(last.X - point.X) + Math.Abs(last.Y - point.Y) >= 1)
                {
                    pen.Points.Add(new PointD(point.X, point.Y));
                    InvalidateVisual();
                }

                e.Handled = true;
                return;
            }

            if (_draftAnnotation is not null)
            {
                UpdateDraft(_draftAnnotation, origin, point);
                if (_draftAnnotation is BlurAnnotation blur)
                {
                    InvalidateBlurPreview(blur);
                }

                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
            e.Handled = true;

            var document = _owner._document;
            if (document.CurrentTool == AnnotationTool.Crop && _cropPreview is { } crop && crop.Width >= 4 && crop.Height >= 4)
            {
                if (!_mutationPushed)
                {
                    _owner._history.Push(document);
                }

                document.ApplyCrop(crop);
                ReloadBaseBitmap();
                _owner.ReturnToSelect();
            }
            else if (_draftAnnotation is not null)
            {
                _owner.ReturnToSelect();
            }

            _dragOrigin = null;
            _draftAnnotation = null;
            _activeHandle = null;
            _isMovingSelection = false;
            _cropPreview = null;
            _mutationPushed = false;
            InvalidateVisual();
            _owner.DocumentChanged?.Invoke(_owner, EventArgs.Empty);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_baseBitmap is not null)
            {
                context.DrawImage(_baseBitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
            }

            foreach (var annotation in _owner._document.Annotations)
            {
                DrawAnnotation(context, annotation, selected: ReferenceEquals(annotation, _owner._document.SelectedAnnotation));
            }

            if (_cropPreview is { } crop)
            {
                context.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                    new Pen(Brushes.White, 1),
                    new Rect(crop.X, crop.Y, crop.Width, crop.Height));
            }
        }

        private void EnsureMutationPushed()
        {
            if (_mutationPushed)
            {
                return;
            }

            _owner._history.Push(_owner._document);
            _mutationPushed = true;
        }

        private Annotation? CreateDraft(Point point)
        {
            var document = _owner._document;
            return document.CurrentTool switch
            {
                AnnotationTool.Arrow => new ArrowAnnotation
                {
                    ColorArgb = document.CurrentColorArgb,
                    StrokeWidth = document.CurrentStrokeWidth,
                    StartX = point.X,
                    StartY = point.Y,
                    EndX = point.X,
                    EndY = point.Y
                },
                AnnotationTool.Rectangle => new RectangleAnnotation
                {
                    ColorArgb = document.CurrentColorArgb,
                    StrokeWidth = document.CurrentStrokeWidth,
                    X = point.X,
                    Y = point.Y
                },
                AnnotationTool.Highlight => new HighlightAnnotation
                {
                    ColorArgb = document.CurrentColorArgb,
                    X = point.X,
                    Y = point.Y
                },
                AnnotationTool.Blur => new BlurAnnotation
                {
                    X = point.X,
                    Y = point.Y
                },
                AnnotationTool.Pen => CreatePenDraft(point),
                _ => null
            };
        }

        private PenAnnotation CreatePenDraft(Point point)
        {
            var pen = new PenAnnotation
            {
                ColorArgb = _owner._document.CurrentColorArgb,
                StrokeWidth = _owner._document.CurrentStrokeWidth
            };
            pen.Points.Add(new PointD(point.X, point.Y));
            return pen;
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

        private void InvalidateBlurPreview(BlurAnnotation blur)
        {
            if (_blurPreviewCache.Remove(blur.Id, out var existing))
            {
                existing.Dispose();
            }
        }

        private void DrawAnnotation(DrawingContext context, Annotation annotation, bool selected)
        {
            switch (annotation)
            {
                case ArrowAnnotation arrow:
                {
                    var color = Color.FromUInt32(arrow.ColorArgb);
                    var pen = new Pen(new SolidColorBrush(color), arrow.StrokeWidth);
                    context.DrawLine(pen, new Point(arrow.StartX, arrow.StartY), new Point(arrow.EndX, arrow.EndY));
                    DrawArrowHead(context, pen, arrow);
                    break;
                }
                case TextAnnotation text when !ReferenceEquals(text, _owner._editingText):
                {
                    var textColor = Color.FromUInt32(text.ColorArgb);
                    var weight = text.IsBold ? FontWeight.Bold : FontWeight.Normal;
                    var style = text.IsItalic ? FontStyle.Italic : FontStyle.Normal;
                    var formatted = new FormattedText(
                        text.Text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI", style, weight),
                        text.FontSize,
                        new SolidColorBrush(textColor));
                    context.DrawText(formatted, new Point(text.X, text.Y - text.FontSize));
                    break;
                }
                case RectangleAnnotation rect:
                {
                    var rectBounds = AnnotationGeometry.NormalizeRect(rect.X, rect.Y, rect.Width, rect.Height);
                    context.DrawRectangle(
                        null,
                        new Pen(new SolidColorBrush(Color.FromUInt32(rect.ColorArgb)), rect.StrokeWidth),
                        new Rect(rectBounds.X, rectBounds.Y, rectBounds.Width, rectBounds.Height));
                    break;
                }
                case HighlightAnnotation highlight:
                {
                    var highlightBounds = AnnotationGeometry.NormalizeRect(highlight.X, highlight.Y, highlight.Width, highlight.Height);
                    var fill = Color.FromUInt32(highlight.ColorArgb);
                    fill = Color.FromArgb(highlight.Opacity, fill.R, fill.G, fill.B);
                    context.FillRectangle(
                        new SolidColorBrush(fill),
                        new Rect(highlightBounds.X, highlightBounds.Y, highlightBounds.Width, highlightBounds.Height));
                    break;
                }
                case BlurAnnotation blur:
                    DrawBlurPreview(context, blur);
                    break;
                case PenAnnotation pen:
                    DrawPen(context, pen);
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

        private void DrawBlurPreview(DrawingContext context, BlurAnnotation blur)
        {
            var bounds = AnnotationGeometry.NormalizeRect(blur.X, blur.Y, blur.Width, blur.Height);
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                return;
            }

            if (!_blurPreviewCache.TryGetValue(blur.Id, out var preview))
            {
                var png = ImagePixelOps.CreatePixelatedRegionPng(
                    _owner._document.PngBytes,
                    bounds,
                    blur.PixelSize,
                    out _,
                    out _);
                if (png is null)
                {
                    return;
                }

                using var stream = new MemoryStream(png);
                preview = new Bitmap(stream);
                _blurPreviewCache[blur.Id] = preview;
            }

            context.DrawImage(preview, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            context.DrawRectangle(
                null,
                new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1),
                new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        private static void DrawPen(DrawingContext context, PenAnnotation pen)
        {
            if (pen.Points.Count == 0)
            {
                return;
            }

            var stroke = new Pen(new SolidColorBrush(Color.FromUInt32(pen.ColorArgb)), pen.StrokeWidth)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            if (pen.Points.Count == 1)
            {
                var point = pen.Points[0];
                context.DrawEllipse(
                    new SolidColorBrush(Color.FromUInt32(pen.ColorArgb)),
                    null,
                    new Point(point.X, point.Y),
                    pen.StrokeWidth / 2,
                    pen.StrokeWidth / 2);
                return;
            }

            var geometry = new StreamGeometry();
            using (var geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(new Point(pen.Points[0].X, pen.Points[0].Y), false);
                for (var index = 1; index < pen.Points.Count; index++)
                {
                    geometryContext.LineTo(new Point(pen.Points[index].X, pen.Points[index].Y));
                }
            }

            context.DrawGeometry(null, stroke, geometry);
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
    }
}
