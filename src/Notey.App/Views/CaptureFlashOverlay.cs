using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Notey.App.Views;

/// <summary>
/// Brief topmost flash around a captured screen region to confirm a screenshot was taken.
/// </summary>
public sealed class CaptureFlashOverlay : Window
{
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(380);

    private CaptureFlashOverlay(PixelRect screenBounds, double scaling)
    {
        Title = "Capture flash";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;

        Position = new PixelPoint(screenBounds.X, screenBounds.Y);
        Width = Math.Max(1, screenBounds.Width / Math.Max(0.1, scaling));
        Height = Math.Max(1, screenBounds.Height / Math.Max(0.1, scaling));

        Content = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 173, 198, 255)),
            BorderThickness = new Thickness(Math.Clamp(Math.Min(Width, Height) * 0.012, 3, 8)),
            CornerRadius = new CornerRadius(2)
        };
    }

    public static async Task ShowAsync(int screenX, int screenY, int width, int height)
    {
        if (width < 2 || height < 2)
        {
            return;
        }

        var probe = new Window();
        try
        {
            var screens = probe.Screens.All;
            var screen = screens.FirstOrDefault(candidate =>
            {
                var bounds = candidate.Bounds;
                return screenX >= bounds.X
                       && screenY >= bounds.Y
                       && screenX < bounds.X + bounds.Width
                       && screenY < bounds.Y + bounds.Height;
            }) ?? probe.Screens.Primary ?? screens.FirstOrDefault();

            var scaling = screen?.Scaling is > 0 ? screen.Scaling : 1;
            var flashBounds = new PixelRect(screenX, screenY, width, height);
            var overlay = new CaptureFlashOverlay(flashBounds, scaling);
            overlay.Show();

            try
            {
                var border = (Border)overlay.Content!;
                border.Opacity = 1;
                var animation = new Animation
                {
                    Duration = FlashDuration,
                    Easing = new QuadraticEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0),
                            Setters = { new Setter(OpacityProperty, 1.0) }
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1),
                            Setters = { new Setter(OpacityProperty, 0.0) }
                        }
                    }
                };
                await animation.RunAsync(border);
            }
            finally
            {
                overlay.Close();
            }
        }
        finally
        {
            probe.Close();
        }
    }

    public static Task ShowForCaptureAsync(int screenX, int screenY, int width, int height)
    {
        try
        {
            return Dispatcher.UIThread.InvokeAsync(() => ShowAsync(screenX, screenY, width, height));
        }
        catch
        {
            return Task.CompletedTask;
        }
    }
}
