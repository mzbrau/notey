using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Notey.Core.Platform;

namespace Notey.App.ScreenshotEditor;

public sealed class ScreenshotEditorWindow : Window
{
    private readonly ScreenshotEditDocument _document;
    private readonly AnnotationCanvas _canvas;
    private readonly IImageClipboardService _clipboard;
    private readonly INoteImageInserter _noteImageInserter;
    private readonly TextBlock _statusText = new()
    {
        Foreground = Brush.Parse("#C2C6D6"),
        VerticalAlignment = VerticalAlignment.Center
    };

    private ScreenshotEditorWindow(
        ScreenshotEditDocument document,
        IImageClipboardService clipboard,
        INoteImageInserter noteImageInserter)
    {
        _document = document;
        _clipboard = clipboard;
        _noteImageInserter = noteImageInserter;
        _canvas = new AnnotationCanvas(document);
        _canvas.DocumentChanged += (_, _) => UpdateStatus();

        Title = "Screenshot Editor";
        Width = Math.Clamp(document.Width + 48, 640, 1400);
        Height = Math.Clamp(document.Height + 140, 480, 900);
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush.Parse("#10131A");
        Content = BuildContent();
        UpdateStatus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Delete)
            {
                _canvas.DeleteSelected();
                e.Handled = true;
            }
        };
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
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12),
            Children =
            {
                CreateActionButton("Save", async () => await SaveAsync()),
                CreateActionButton("Copy", async () => await CopyAsync()),
                CreateActionButton("Add to Notey", async () => await InsertAsync()),
                CreateToolButton("Select", AnnotationTool.Select),
                CreateToolButton("Arrow", AnnotationTool.Arrow),
                CreateToolButton("Text", AnnotationTool.Text),
                CreateToolButton("Rectangle", AnnotationTool.Rectangle),
                CreateToolButton("Highlight", AnnotationTool.Highlight),
                CreateToolButton("Blur", AnnotationTool.Blur),
                CreateToolButton("Crop", AnnotationTool.Crop),
                CreateColorPalette(),
                _statusText
            }
        };

        return new Border
        {
            Background = Brush.Parse("#161A22"),
            BorderBrush = Brush.Parse("#424754"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            [DockPanel.DockProperty] = Dock.Top,
            Child = panel
        };
    }

    private Button CreateActionButton(string label, Func<Task> action)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 88,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
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

    private ToggleButton CreateToolButton(string label, AnnotationTool tool)
    {
        var button = new ToggleButton
        {
            Content = label,
            IsChecked = _document.CurrentTool == tool,
            MinWidth = 72
        };
        button.Click += (_, _) =>
        {
            _document.CurrentTool = tool;
            foreach (var child in ((StackPanel)button.Parent!).Children.OfType<ToggleButton>())
            {
                child.IsChecked = ReferenceEquals(child, button);
            }
        };
        return button;
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

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
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
            swatch.Click += (_, _) =>
            {
                _document.CurrentColorArgb = selected;
                if (_document.SelectedAnnotation is { } annotation)
                {
                    annotation.ColorArgb = selected;
                    _canvas.NotifyExternalChange();
                }
            };
            panel.Children.Add(swatch);
        }

        return panel;
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
    }
}
