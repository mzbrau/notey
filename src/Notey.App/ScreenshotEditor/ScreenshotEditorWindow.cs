using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Notey.Core.Platform;

namespace Notey.App.ScreenshotEditor;

public sealed class ScreenshotEditorWindow : Window
{
    private readonly ScreenshotEditDocument _document;
    private readonly ScreenshotEditHistory _history = new();
    private readonly AnnotationCanvas _canvas;
    private readonly IImageClipboardService _clipboard;
    private readonly INoteImageInserter _noteImageInserter;
    private readonly TextBlock _statusText = new()
    {
        Foreground = Brush.Parse("#C2C6D6"),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0)
    };

    private readonly Border _currentColorChip = new()
    {
        Width = 28,
        Height = 28,
        CornerRadius = new CornerRadius(4),
        BorderBrush = Brush.Parse("#E8EAED"),
        BorderThickness = new Thickness(2),
        Margin = new Thickness(4, 0)
    };

    private readonly NumericUpDown _thicknessInput = new()
    {
        Minimum = 1,
        Maximum = 12,
        Increment = 1,
        Width = 72,
        FormatString = "0",
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly NumericUpDown _fontSizeInput = new()
    {
        Minimum = 10,
        Maximum = 96,
        Increment = 1,
        Width = 72,
        FormatString = "0",
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly ToggleButton _boldButton = new() { Content = "B", MinWidth = 32, FontWeight = FontWeight.Bold };
    private readonly ToggleButton _italicButton = new() { Content = "I", MinWidth = 32, FontStyle = FontStyle.Italic };
    private readonly Dictionary<AnnotationTool, ToggleButton> _toolButtons = new();
    private bool _suppressAppearanceEvents;

    private ScreenshotEditorWindow(
        ScreenshotEditDocument document,
        IImageClipboardService clipboard,
        INoteImageInserter noteImageInserter)
    {
        _document = document;
        _clipboard = clipboard;
        _noteImageInserter = noteImageInserter;
        _canvas = new AnnotationCanvas(document, _history);
        _canvas.DocumentChanged += (_, _) => UpdateStatus();
        _canvas.ToolReturnedToSelect += (_, _) => SyncToolButtons();
        _canvas.AppearanceChanged += (_, _) => SyncAppearanceControls();

        Title = "Screenshot Editor";
        MinWidth = 720;
        MinHeight = 480;
        Width = Math.Max(MinWidth, Math.Min(document.Width + 48, 1400));
        Height = Math.Max(MinHeight, Math.Min(document.Height + 140, 900));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush.Parse("#10131A");
        Content = BuildContent();
        SyncAppearanceControls();
        UpdateStatus();
        KeyDown += OnKeyDown;
        Opened += (_, _) => ApplyIdealWindowSize();
    }

    public static ScreenshotEditorWindow ShowNew(
        byte[] pngBytes,
        int width,
        int height,
        IImageClipboardService clipboard,
        INoteImageInserter noteImageInserter)
    {
        var document = new ScreenshotEditDocument(pngBytes, width, height);
        var window = new ScreenshotEditorWindow(document, clipboard, noteImageInserter);
        window.Show();
        return window;
    }

    private void ApplyIdealWindowSize()
    {
        const double chromeWidth = 48;
        const double chromeHeight = 140;
        var idealWidth = Math.Max(MinWidth, _document.Width + chromeWidth);
        var idealHeight = Math.Max(MinHeight, _document.Height + chromeHeight);

        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var working = screen.WorkingArea;
            var maxWidth = working.Width * 0.9 / Math.Max(1, screen.Scaling);
            var maxHeight = working.Height * 0.9 / Math.Max(1, screen.Scaling);
            idealWidth = Math.Min(idealWidth, maxWidth);
            idealHeight = Math.Min(idealHeight, maxHeight);
        }
        else
        {
            idealWidth = Math.Min(idealWidth, 1400);
            idealHeight = Math.Min(idealHeight, 900);
        }

        Width = idealWidth;
        Height = idealHeight;
    }

    private Control BuildContent()
    {
        return new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                BuildToolbar(),
                new Border
                {
                    Background = Brush.Parse("#0B0D12"),
                    Padding = new Thickness(16),
                    Child = new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new Border
                        {
                            Background = Brush.Parse("#1D2027"),
                            Child = _canvas,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        }
                    }
                }
            }
        };
    }

    private Control BuildToolbar()
    {
        var tools = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8)
        };

        tools.Children.Add(CreateActionIconButton(
            "M5,3 H19 V5 H5 Z M5,11 H19 V13 H5 Z M5,19 H19 V21 H5 Z",
            "Save",
            async () => await SaveAsync()));
        tools.Children.Add(CreateActionIconButton(
            "M8,4 H16 V10 H20 L12,18 L4,10 H8 Z M4,20 H20 V22 H4 Z",
            "Copy",
            async () => await CopyAsync()));
        tools.Children.Add(CreateActionIconButton(
            "M12,3 L4,9 V21 H10 V14 H14 V21 H20 V9 Z",
            "Add to Notey",
            async () => await InsertAsync()));
        tools.Children.Add(CreateActionIconButton(
            "M9,4 L4,12 L9,20 V15 H20 V9 H9 Z",
            "Undo (Ctrl+Z)",
            () =>
            {
                _canvas.Undo();
                SyncAppearanceControls();
                SyncToolButtons();
                return Task.CompletedTask;
            }));

        tools.Children.Add(CreateSeparator());
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Select, "M6,3 L6,17 L10,13 L13,20 L15,19 L12,12 L18,12 Z", "Select (Ctrl+1)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Arrow, "M4,20 L16,8 M12,8 H16 V12", "Arrow (Ctrl+2)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Rectangle, "M5,5 H19 V19 H5 Z", "Rectangle (Ctrl+3)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Highlight, "M5,7 H19 V17 H5 Z", "Highlight (Ctrl+4)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Blur, "M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12 M4,12 H20", "Blur (Ctrl+5)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Pen, "M4,20 L6,14 L16,4 L20,8 L10,18 Z", "Pen (Ctrl+6)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Text, "M6,5 H18 V8 H13 V19 H11 V8 H6 Z", "Text (Ctrl+7)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Eyedropper, "M15,3 L21,9 L12,18 L6,18 L6,12 Z M4,20 H10", "Eyedropper (Ctrl+8)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.PaintBucket, "M4,12 L12,4 L20,12 L12,20 Z M8,20 H18", "Paint bucket (Ctrl+9)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Crop, "M6,2 V14 H18 V18 H14 V22 H12 V18 H2 V16 H6 V6 H2 V4 H6 V2 M10,6 H18 V14", "Crop (Ctrl+0)"));

        tools.Children.Add(CreateSeparator());
        tools.Children.Add(new TextBlock
        {
            Text = "Color",
            Foreground = Brush.Parse("#C2C6D6"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0)
        });
        tools.Children.Add(_currentColorChip);
        tools.Children.Add(CreateColorPalette());

        tools.Children.Add(CreateSeparator());
        tools.Children.Add(new TextBlock
        {
            Text = "Thickness",
            Foreground = Brush.Parse("#C2C6D6"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0)
        });
        _thicknessInput.Value = (decimal)_document.CurrentStrokeWidth;
        _thicknessInput.ValueChanged += (_, _) =>
        {
            if (_suppressAppearanceEvents || _thicknessInput.Value is not { } value)
            {
                return;
            }

            var width = (double)value;
            _document.CurrentStrokeWidth = width;
            if (_document.SelectedAnnotation is IStrokeAnnotation stroke)
            {
                _history.Push(_document);
                stroke.StrokeWidth = width;
                _canvas.NotifyExternalChange();
            }
        };
        tools.Children.Add(_thicknessInput);

        tools.Children.Add(CreateSeparator());
        tools.Children.Add(_boldButton);
        tools.Children.Add(_italicButton);
        tools.Children.Add(new TextBlock
        {
            Text = "Size",
            Foreground = Brush.Parse("#C2C6D6"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0)
        });
        _fontSizeInput.Value = 24;
        _fontSizeInput.ValueChanged += (_, _) =>
        {
            if (!_suppressAppearanceEvents)
            {
                ApplyTextFormatting();
            }
        };
        _boldButton.Click += (_, _) =>
        {
            if (!_suppressAppearanceEvents)
            {
                ApplyTextFormatting();
            }
        };
        _italicButton.Click += (_, _) =>
        {
            if (!_suppressAppearanceEvents)
            {
                ApplyTextFormatting();
            }
        };
        tools.Children.Add(_fontSizeInput);
        tools.Children.Add(_statusText);

        return new Border
        {
            Background = Brush.Parse("#161A22"),
            BorderBrush = Brush.Parse("#424754"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            [DockPanel.DockProperty] = Dock.Top,
            Child = tools
        };
    }

    private static Control CreateSeparator() => new Border
    {
        Width = 1,
        Height = 24,
        Background = Brush.Parse("#424754"),
        Margin = new Thickness(6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private Button CreateActionIconButton(string geometry, string tooltip, Func<Task> action)
    {
        var button = CreateIconButton(geometry);
        ToolTip.SetTip(button, tooltip);
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException or ArgumentException)
            {
                _statusText.Text = ex.Message;
            }
        };
        return button;
    }

    private ToggleButton CreateToolIconButton(AnnotationTool tool, string geometry, string tooltip)
    {
        var button = new ToggleButton
        {
            Width = 34,
            Height = 34,
            MinWidth = 34,
            Padding = new Thickness(0),
            IsChecked = _document.CurrentTool == tool,
            Content = new PathIcon
            {
                Data = Geometry.Parse(geometry),
                Width = 16,
                Height = 16,
                Foreground = Brush.Parse("#E8EAED")
            }
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => SelectTool(tool);
        _toolButtons[tool] = button;
        return button;
    }

    private static Button CreateIconButton(string geometry)
    {
        return new Button
        {
            Width = 34,
            Height = 34,
            MinWidth = 34,
            Padding = new Thickness(0),
            Content = new PathIcon
            {
                Data = Geometry.Parse(geometry),
                Width = 16,
                Height = 16,
                Foreground = Brush.Parse("#E8EAED")
            }
        };
    }

    private void SelectTool(AnnotationTool tool)
    {
        _document.CurrentTool = tool;
        SyncToolButtons();
    }

    private void SyncToolButtons()
    {
        foreach (var (tool, button) in _toolButtons)
        {
            button.IsChecked = tool == _document.CurrentTool;
        }
    }

    private Control CreateColorPalette()
    {
        uint[] colors =
        [
            0xFFE53935,
            0xFFFF9800,
            0xFFFFEB3B,
            0xFF4CAF50,
            0xFF2196F3,
            0xFF9C27B0,
            0xFFFFFFFF,
            0xFF212121
        ];

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var color in colors)
        {
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromUInt32(color)),
                BorderBrush = Brush.Parse("#424754"),
                BorderThickness = new Thickness(1)
            };
            var selected = color;
            swatch.Click += (_, _) => ApplyColor(selected);
            panel.Children.Add(swatch);
        }

        return panel;
    }

    private void ApplyColor(uint color)
    {
        _document.CurrentColorArgb = color;
        if (_document.SelectedAnnotation is { } annotation)
        {
            _history.Push(_document);
            annotation.ColorArgb = color;
            _canvas.NotifyExternalChange();
        }

        SyncAppearanceControls();
    }

    private void ApplyTextFormatting()
    {
        if (_document.SelectedAnnotation is not TextAnnotation text)
        {
            return;
        }

        _history.Push(_document);
        text.IsBold = _boldButton.IsChecked == true;
        text.IsItalic = _italicButton.IsChecked == true;
        if (_fontSizeInput.Value is { } size)
        {
            text.FontSize = (double)size;
        }

        _canvas.NotifyExternalChange();
    }

    private void SyncAppearanceControls()
    {
        _suppressAppearanceEvents = true;
        try
        {
            _currentColorChip.Background = new SolidColorBrush(Color.FromUInt32(_document.CurrentColorArgb));
            _thicknessInput.Value = (decimal)_document.CurrentStrokeWidth;

            if (_document.SelectedAnnotation is TextAnnotation text)
            {
                _boldButton.IsChecked = text.IsBold;
                _italicButton.IsChecked = text.IsItalic;
                _fontSizeInput.Value = (decimal)text.FontSize;
            }

            if (_document.SelectedAnnotation is IStrokeAnnotation stroke)
            {
                _thicknessInput.Value = (decimal)stroke.StrokeWidth;
            }
        }
        finally
        {
            _suppressAppearanceEvents = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.D1:
                    SelectTool(AnnotationTool.Select);
                    e.Handled = true;
                    return;
                case Key.D2:
                    SelectTool(AnnotationTool.Arrow);
                    e.Handled = true;
                    return;
                case Key.D3:
                    SelectTool(AnnotationTool.Rectangle);
                    e.Handled = true;
                    return;
                case Key.D4:
                    SelectTool(AnnotationTool.Highlight);
                    e.Handled = true;
                    return;
                case Key.D5:
                    SelectTool(AnnotationTool.Blur);
                    e.Handled = true;
                    return;
                case Key.D6:
                    SelectTool(AnnotationTool.Pen);
                    e.Handled = true;
                    return;
                case Key.D7:
                    SelectTool(AnnotationTool.Text);
                    e.Handled = true;
                    return;
                case Key.D8:
                    SelectTool(AnnotationTool.Eyedropper);
                    e.Handled = true;
                    return;
                case Key.D9:
                    SelectTool(AnnotationTool.PaintBucket);
                    e.Handled = true;
                    return;
                case Key.D0:
                    SelectTool(AnnotationTool.Crop);
                    e.Handled = true;
                    return;
                case Key.Z:
                    _canvas.Undo();
                    SyncAppearanceControls();
                    SyncToolButtons();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Delete)
        {
            _canvas.DeleteSelected();
            e.Handled = true;
        }
    }

    private async Task SaveAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save screenshot",
            SuggestedFileName = $"notey-screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            DefaultExtension = "png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG image") { Patterns = ["*.png"] }
            ]
        });

        if (file is null)
        {
            return;
        }

        var bytes = AnnotationCompositor.FlattenToPng(_document);
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
        _statusText.Text = "Saved";
    }

    private async Task CopyAsync()
    {
        var bytes = AnnotationCompositor.FlattenToPng(_document);
        await _clipboard.CopyPngAsync(bytes);
        _statusText.Text = "Copied";
    }

    private async Task InsertAsync()
    {
        var bytes = AnnotationCompositor.FlattenToPng(_document);
        await _noteImageInserter.InsertPngImageAsync(bytes);
        _statusText.Text = "Added to Notey";
    }

    private void UpdateStatus()
    {
        _statusText.Text = $"{_document.Width}×{_document.Height} · {_document.Annotations.Count} annotations";
        SyncAppearanceControls();
    }
}
