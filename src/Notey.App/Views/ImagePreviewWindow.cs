using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Notey.App.Views;

public sealed class ImagePreviewWindow : Window
{
    private readonly Bitmap? _bitmap;

    private ImagePreviewWindow(PreviewContent content)
    {
        _bitmap = content.Bitmap;

        Title = "Image preview";
        Width = 780;
        Height = 620;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;

        var details = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                CreateDetailBlock("Path", content.RelativePath),
                CreateDetailBlock("Modified", FormatModifiedAt(content.ModifiedAt)),
                CreateDetailBlock("Size", FormatFileSize(content.FileSize)),
                CreateDetailBlock("Dimensions", content.Dimensions)
            }
        };

        Control previewContent = content.Bitmap is null
            ? new Border
            {
                Background = new SolidColorBrush(Color.Parse("#0B0E15")),
                BorderBrush = new SolidColorBrush(Color.Parse("#424754")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Avalonia.Thickness(18),
                Child = new TextBlock
                {
                    Text = content.ErrorMessage,
                    Foreground = new SolidColorBrush(Color.Parse("#C2C6D6")),
                    TextWrapping = TextWrapping.Wrap
                }
            }
            : new Border
            {
                Background = new SolidColorBrush(Color.Parse("#0B0E15")),
                BorderBrush = new SolidColorBrush(Color.Parse("#424754")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Image
                    {
                        Source = content.Bitmap,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };

        var closeButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 92
        };
        closeButton.Click += (_, _) => Close();

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#10131A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#424754")),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(24),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                RowSpacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Image preview",
                        Foreground = new SolidColorBrush(Color.Parse("#E1E2EC")),
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Preview the embedded image and its file details.",
                        Foreground = new SolidColorBrush(Color.Parse("#C2C6D6")),
                        TextWrapping = TextWrapping.Wrap
                    }.WithGridRow(1),
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("2*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            previewContent,
                            new Border
                            {
                                Background = new SolidColorBrush(Color.Parse("#1D2027")),
                                BorderBrush = new SolidColorBrush(Color.Parse("#424754")),
                                BorderThickness = new Avalonia.Thickness(1),
                                CornerRadius = new CornerRadius(4),
                                Padding = new Avalonia.Thickness(16),
                                Child = details
                            }.WithGridColumn(1)
                        }
                    }.WithGridRow(2),
                    closeButton.WithGridRow(3)
                }
            }
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _bitmap?.Dispose();
        base.OnClosed(e);
    }

    public static async Task ShowAsync(Window owner, string absolutePath, string relativePath, CancellationToken cancellationToken = default)
    {
        var dialog = new ImagePreviewWindow(await LoadPreviewContentAsync(absolutePath, relativePath, cancellationToken));
        await dialog.ShowDialog(owner);
    }

    private static async Task<PreviewContent> LoadPreviewContentAsync(string absolutePath, string relativePath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var fileInfo = new FileInfo(absolutePath);
            if (!fileInfo.Exists)
            {
                return PreviewContent.Error(relativePath, absolutePath, "The image file could not be found.");
            }

            try
            {
                using var stream = fileInfo.OpenRead();
                var bitmap = new Bitmap(stream);
                return PreviewContent.Success(
                    bitmap,
                    relativePath,
                    absolutePath,
                    new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                    fileInfo.Length,
                    $"{bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
            }
            catch (IOException)
            {
                return PreviewContent.Error(relativePath, absolutePath, "The image file could not be read.");
            }
            catch (UnauthorizedAccessException)
            {
                return PreviewContent.Error(relativePath, absolutePath, "Notey does not have permission to open this image file.");
            }
            catch (ArgumentException)
            {
                return PreviewContent.Error(relativePath, absolutePath, "The embedded image path is invalid.");
            }
        }, cancellationToken);
    }

    private static Control CreateDetailBlock(string label, string value)
    {
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = label.ToUpperInvariant(),
                    Foreground = new SolidColorBrush(Color.Parse("#8C909F")),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = value,
                    Foreground = new SolidColorBrush(Color.Parse("#E1E2EC")),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private static string FormatFileSize(long bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;

        return bytes >= mb
            ? $"{bytes / mb:0.0} MB"
            : bytes >= kb
                ? $"{bytes / kb:0.0} KB"
                : $"{bytes} B";
    }

    private static string FormatModifiedAt(DateTimeOffset modifiedAt)
    {
        return modifiedAt == DateTimeOffset.MinValue
            ? "Unavailable"
            : modifiedAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    private sealed record PreviewContent(
        Bitmap? Bitmap,
        string RelativePath,
        string AbsolutePath,
        DateTimeOffset ModifiedAt,
        long FileSize,
        string Dimensions,
        string ErrorMessage)
    {
        public static PreviewContent Success(
            Bitmap bitmap,
            string relativePath,
            string absolutePath,
            DateTimeOffset modifiedAt,
            long fileSize,
            string dimensions)
        {
            return new PreviewContent(bitmap, relativePath, absolutePath, modifiedAt, fileSize, dimensions, string.Empty);
        }

        public static PreviewContent Error(string relativePath, string absolutePath, string errorMessage)
        {
            return new PreviewContent(
                null,
                relativePath,
                absolutePath,
                File.Exists(absolutePath)
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(absolutePath), TimeSpan.Zero)
                    : DateTimeOffset.MinValue,
                File.Exists(absolutePath) ? new FileInfo(absolutePath).Length : 0,
                "Unavailable",
                errorMessage);
        }
    }
}

file static class ImagePreviewWindowControlExtensions
{
    public static T WithGridRow<T>(this T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    public static T WithGridColumn<T>(this T control, int column)
        where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
