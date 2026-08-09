using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Notey.Core.Platform;
using Optris.Icons.Avalonia;

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

    private readonly Slider _thicknessSlider = new()
    {
        Minimum = 1,
        Maximum = 12,
        Width = 100,
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly NumericUpDown _thicknessInput = new()
    {
        Minimum = 1,
        Maximum = 12,
        Increment = 1,
        Width = 56,
        FormatString = "0",
        ShowButtonSpinner = false,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly Slider _fillToleranceSlider = new()
    {
        Minimum = 0,
        Maximum = 100,
        Width = 100,
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly NumericUpDown _fillToleranceInput = new()
    {
        Minimum = 0,
        Maximum = 100,
        Increment = 1,
        Width = 56,
        FormatString = "0",
        ShowButtonSpinner = false,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly NumericUpDown _fontSizeInput = new()
    {
        Minimum = 10,
        Maximum = 96,
        Increment = 1,
        Width = 72,
        FormatString = "0",
        ShowButtonSpinner = false,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly ToggleButton _boldButton = new();
    private readonly ToggleButton _italicButton = new();
    private readonly ToggleButton _textBackgroundButton = new();
    private readonly Dictionary<AnnotationTool, ToggleButton> _toolButtons = new();
    private readonly StackPanel _strokePanel = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _textPanel = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _fillPanel = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly Control _strokeSeparator = CreateSeparator();
    private readonly Control _textSeparator = CreateSeparator();
    private readonly Control _fillSeparator = CreateSeparator();
    private bool _suppressAppearanceEvents;
    private bool _strokeHistoryPushed;
    private bool _textBackgroundAuto;

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
        _canvas.ToolReturnedToSelect += (_, _) =>
        {
            SyncToolButtons();
            SyncAppearanceControls();
        };
        _canvas.AppearanceChanged += (_, _) => SyncAppearanceControls();

        Title = "Screenshot Editor";
        MinWidth = 720;
        MinHeight = 480;
        Width = Math.Max(MinWidth, Math.Min(document.Width + 48, 1400));
        Height = Math.Max(MinHeight, Math.Min(document.Height + 140, 900));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush.Parse("#10131A");
        Styles.AddRange(CreateToolbarStyles());
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

        // Actions
        tools.Children.Add(CreateActionIconButton("mdi-content-save", "Save", async () => await SaveAsync()));
        tools.Children.Add(CreateActionIconButton("mdi-content-copy", "Copy", async () => await CopyAsync()));
        tools.Children.Add(CreateActionIconButton("mdi-note-plus-outline", "Add to Notey", async () => await InsertAsync()));
        tools.Children.Add(CreateActionIconButton(
            "mdi-undo",
            "Undo (Ctrl+Z)",
            () =>
            {
                _canvas.Undo();
                SyncAppearanceControls();
                SyncToolButtons();
                return Task.CompletedTask;
            }));
        tools.Children.Add(CreateActionIconButton(
            "mdi-redo",
            "Redo (Ctrl+Y)",
            () =>
            {
                _canvas.Redo();
                SyncAppearanceControls();
                SyncToolButtons();
                return Task.CompletedTask;
            }));

        // Select
        tools.Children.Add(CreateSeparator());
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Select, "mdi-cursor-default", "Select (Ctrl+1)"));

        // Draw
        tools.Children.Add(CreateSeparator());
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Arrow, "mdi-arrow-top-right", "Arrow (Ctrl+2)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Rectangle, "mdi-rectangle-outline", "Rectangle (Ctrl+3)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Highlight, "mdi-marker", "Highlight (Ctrl+4)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Blur, "mdi-blur", "Blur (Ctrl+5)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Pen, "mdi-pencil", "Pen (Ctrl+6)"));

        // Text
        tools.Children.Add(CreateSeparator());
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Text, "mdi-format-text", "Text (Ctrl+7)"));

        // Color tools
        tools.Children.Add(CreateSeparator());
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Eyedropper, "mdi-eyedropper", "Eyedropper (Ctrl+8)"));
        tools.Children.Add(CreateToolIconButton(AnnotationTool.PaintBucket, "mdi-format-color-fill", "Paint bucket (Ctrl+9)"));

        // Crop
        tools.Children.Add(CreateSeparator());
        tools.Children.Add(CreateToolIconButton(AnnotationTool.Crop, "mdi-crop", "Crop (Ctrl+0)"));

        // Shared color
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

        // Stroke context panel
        tools.Children.Add(_strokeSeparator);
        _strokePanel.Children.Add(new TextBlock
        {
            Text = "Width",
            Foreground = Brush.Parse("#C2C6D6"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0)
        });
        _thicknessSlider.Value = _document.CurrentStrokeWidth;
        _thicknessInput.Value = (decimal)_document.CurrentStrokeWidth;
        _thicknessSlider.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
        {
            if (_document.SelectedAnnotation is IStrokeAnnotation && !_strokeHistoryPushed)
            {
                _history.Push(_document);
                _strokeHistoryPushed = true;
            }
        }, handledEventsToo: true);
        _thicknessSlider.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            _strokeHistoryPushed = false;
        }, handledEventsToo: true);
        _thicknessSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || _suppressAppearanceEvents)
            {
                return;
            }

            ApplyStrokeWidth(_thicknessSlider.Value, recordHistory: false);
        };
        _thicknessInput.ValueChanged += (_, _) =>
        {
            if (_suppressAppearanceEvents || _thicknessInput.Value is not { } value)
            {
                return;
            }

            ApplyStrokeWidth((double)value, recordHistory: true);
        };
        _strokePanel.Children.Add(_thicknessSlider);
        _strokePanel.Children.Add(_thicknessInput);
        tools.Children.Add(_strokePanel);

        // Text context panel
        tools.Children.Add(_textSeparator);
        ConfigureFormatToggle(_boldButton, "mdi-format-bold", "Bold");
        ConfigureFormatToggle(_italicButton, "mdi-format-italic", "Italic");
        ConfigureFormatToggle(_textBackgroundButton, "mdi-format-color-highlight", "Text background");
        _textPanel.Children.Add(_boldButton);
        _textPanel.Children.Add(_italicButton);
        _textPanel.Children.Add(_textBackgroundButton);
        _textPanel.Children.Add(new TextBlock
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
        _textBackgroundButton.Click += (_, _) =>
        {
            if (!_suppressAppearanceEvents)
            {
                ApplyTextBackgroundToggle();
            }
        };
        _textPanel.Children.Add(_fontSizeInput);
        tools.Children.Add(_textPanel);

        // Fill sensitivity context panel
        tools.Children.Add(_fillSeparator);
        _fillPanel.Children.Add(new TextBlock
        {
            Text = "Sensitivity",
            Foreground = Brush.Parse("#C2C6D6"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0)
        });
        _fillToleranceSlider.Value = _document.CurrentFillTolerance;
        _fillToleranceInput.Value = _document.CurrentFillTolerance;
        _fillToleranceSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || _suppressAppearanceEvents)
            {
                return;
            }

            ApplyFillTolerance((int)Math.Round(_fillToleranceSlider.Value));
        };
        _fillToleranceInput.ValueChanged += (_, _) =>
        {
            if (_suppressAppearanceEvents || _fillToleranceInput.Value is not { } value)
            {
                return;
            }

            ApplyFillTolerance((int)value);
        };
        _fillPanel.Children.Add(_fillToleranceSlider);
        _fillPanel.Children.Add(_fillToleranceInput);
        tools.Children.Add(_fillPanel);

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

    private static IEnumerable<Style> CreateToolbarStyles()
    {
        var buttonBase = new Style(x => x.OfType<Button>().Class("editorToolbarButton"))
        {
            Setters =
            {
                new Setter(RenderTransformProperty, new ScaleTransform(1, 1)),
                new Setter(OpacityProperty, 1.0),
                new Setter(BackgroundProperty, Brushes.Transparent),
                new Setter(CornerRadiusProperty, new CornerRadius(6))
            }
        };

        var buttonHover = new Style(x => x.OfType<Button>().Class("editorToolbarButton").Class(":pointerover"))
        {
            Setters =
            {
                new Setter(BackgroundProperty, Brush.Parse("#2A3140")),
                new Setter(RenderTransformProperty, new ScaleTransform(1.06, 1.06)),
                new Setter(OpacityProperty, 0.95)
            }
        };

        var toggleBase = new Style(x => x.OfType<ToggleButton>().Class("editorToolbarButton"))
        {
            Setters =
            {
                new Setter(RenderTransformProperty, new ScaleTransform(1, 1)),
                new Setter(OpacityProperty, 1.0),
                new Setter(BackgroundProperty, Brushes.Transparent),
                new Setter(CornerRadiusProperty, new CornerRadius(6))
            }
        };

        var toggleHover = new Style(x => x.OfType<ToggleButton>().Class("editorToolbarButton").Class(":pointerover"))
        {
            Setters =
            {
                new Setter(BackgroundProperty, Brush.Parse("#2A3140")),
                new Setter(RenderTransformProperty, new ScaleTransform(1.06, 1.06))
            }
        };

        var toggleChecked = new Style(x => x.OfType<ToggleButton>().Class("editorToolbarButton").Class(":checked"))
        {
            Setters =
            {
                new Setter(BackgroundProperty, Brush.Parse("#3A4660")),
                new Setter(RenderTransformProperty, new ScaleTransform(1.04, 1.04))
            }
        };

        return [buttonBase, buttonHover, toggleBase, toggleHover, toggleChecked];
    }

    private static Control CreateSeparator() => new Border
    {
        Width = 1,
        Height = 24,
        Background = Brush.Parse("#424754"),
        Margin = new Thickness(6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Icon CreateToolbarIcon(string iconValue) => new()
    {
        Value = iconValue,
        FontSize = 16,
        Foreground = Brush.Parse("#E8EAED"),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Transitions CreateButtonTransitions() =>
    [
        new DoubleTransition
        {
            Property = OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(140)
        }
    ];

    private void ConfigureFormatToggle(ToggleButton button, string iconValue, string tooltip)
    {
        button.Width = 34;
        button.Height = 34;
        button.MinWidth = 34;
        button.Padding = new Thickness(0);
        button.Content = CreateToolbarIcon(iconValue);
        button.Classes.Add("editorToolbarButton");
        button.Transitions = CreateButtonTransitions();
        ToolTip.SetTip(button, tooltip);
    }

    private Button CreateActionIconButton(string iconValue, string tooltip, Func<Task> action)
    {
        var button = CreateIconButton(iconValue);
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

    private ToggleButton CreateToolIconButton(AnnotationTool tool, string iconValue, string tooltip)
    {
        var button = new ToggleButton
        {
            Width = 34,
            Height = 34,
            MinWidth = 34,
            Padding = new Thickness(0),
            IsChecked = _document.CurrentTool == tool,
            Content = CreateToolbarIcon(iconValue)
        };
        button.Classes.Add("editorToolbarButton");
        button.Transitions = CreateButtonTransitions();
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => SelectTool(tool);
        _toolButtons[tool] = button;
        return button;
    }

    private static Button CreateIconButton(string iconValue)
    {
        var button = new Button
        {
            Width = 34,
            Height = 34,
            MinWidth = 34,
            Padding = new Thickness(0),
            Content = CreateToolbarIcon(iconValue),
            Transitions = CreateButtonTransitions()
        };
        button.Classes.Add("editorToolbarButton");
        return button;
    }

    private void SelectTool(AnnotationTool tool)
    {
        _document.CurrentTool = tool;
        SyncToolButtons();
        SyncAppearanceControls();
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
            if (annotation is TextAnnotation text && _textBackgroundAuto && text.BackgroundColorArgb is not null)
            {
                text.BackgroundColorArgb = SuggestTextBackground(color);
            }

            _canvas.NotifyExternalChange();
        }

        SyncAppearanceControls();
    }

    private void ApplyTextBackgroundToggle()
    {
        if (_document.SelectedAnnotation is not TextAnnotation text)
        {
            return;
        }

        _history.Push(_document);
        if (_textBackgroundButton.IsChecked == true)
        {
            _textBackgroundAuto = true;
            text.BackgroundColorArgb = SuggestTextBackground(text.ColorArgb);
        }
        else
        {
            _textBackgroundAuto = false;
            text.BackgroundColorArgb = null;
        }

        _canvas.NotifyExternalChange();
    }

    private static uint SuggestTextBackground(uint textColorArgb)
    {
        var r = (textColorArgb >> 16) & 0xFF;
        var g = (textColorArgb >> 8) & 0xFF;
        var b = textColorArgb & 0xFF;
        var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        return luminance >= 140 ? 0xCC10131Au : 0xCCF5F5F5u;
    }

    private void ApplyStrokeWidth(double width, bool recordHistory)
    {
        width = Math.Clamp(width, 1, 12);
        _document.CurrentStrokeWidth = width;
        if (_document.SelectedAnnotation is IStrokeAnnotation stroke && Math.Abs(stroke.StrokeWidth - width) > 0.01)
        {
            if (recordHistory)
            {
                _history.Push(_document);
            }

            stroke.StrokeWidth = width;
            _canvas.NotifyExternalChange();
        }

        _suppressAppearanceEvents = true;
        try
        {
            _thicknessSlider.Value = width;
            _thicknessInput.Value = (decimal)width;
        }
        finally
        {
            _suppressAppearanceEvents = false;
        }
    }

    private void ApplyFillTolerance(int tolerance)
    {
        _document.CurrentFillTolerance = Math.Clamp(tolerance, 0, 100);
        _suppressAppearanceEvents = true;
        try
        {
            _fillToleranceSlider.Value = _document.CurrentFillTolerance;
            _fillToleranceInput.Value = _document.CurrentFillTolerance;
        }
        finally
        {
            _suppressAppearanceEvents = false;
        }
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

            var strokeWidth = _document.SelectedAnnotation is IStrokeAnnotation stroke
                ? stroke.StrokeWidth
                : _document.CurrentStrokeWidth;
            _thicknessSlider.Value = strokeWidth;
            _thicknessInput.Value = (decimal)strokeWidth;

            _fillToleranceSlider.Value = _document.CurrentFillTolerance;
            _fillToleranceInput.Value = _document.CurrentFillTolerance;

            if (_document.SelectedAnnotation is TextAnnotation text)
            {
                _boldButton.IsChecked = text.IsBold;
                _italicButton.IsChecked = text.IsItalic;
                _fontSizeInput.Value = (decimal)text.FontSize;
                _textBackgroundButton.IsChecked = text.BackgroundColorArgb is not null;
                if (text.BackgroundColorArgb is null)
                {
                    _textBackgroundAuto = false;
                }
            }
            else
            {
                _textBackgroundButton.IsChecked = false;
            }

            var showStroke = IsStrokeContextVisible();
            var showText = IsTextContextVisible();
            var showFill = _document.CurrentTool == AnnotationTool.PaintBucket;

            _strokePanel.IsVisible = showStroke;
            _strokeSeparator.IsVisible = showStroke;
            _textPanel.IsVisible = showText;
            _textSeparator.IsVisible = showText;
            _fillPanel.IsVisible = showFill;
            _fillSeparator.IsVisible = showFill;
        }
        finally
        {
            _suppressAppearanceEvents = false;
        }
    }

    private bool IsStrokeContextVisible()
    {
        if (_document.SelectedAnnotation is IStrokeAnnotation)
        {
            return true;
        }

        return _document.CurrentTool is AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Pen;
    }

    private bool IsTextContextVisible()
        => _document.CurrentTool == AnnotationTool.Text
           || _document.SelectedAnnotation is TextAnnotation;

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
                case Key.Y:
                    _canvas.Redo();
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
