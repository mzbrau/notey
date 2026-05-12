using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Notey.App.Views;

public sealed class ScreenSnipSelectionWindow : Window
{
    private readonly SelectionSurface _selectionSurface = new();
    private readonly PixelRect? _screenBounds;
    private readonly double _screenScaling;
    private ScreenSnipSelection? _selection;
    private Action? _requestGlobalCancel;

    private ScreenSnipSelectionWindow(PixelRect? screenBounds, double screenScaling)
    {
        _screenBounds = screenBounds;
        _screenScaling = screenScaling <= 0 ? 1 : screenScaling;

        Title = "Select screen region";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Content = _selectionSurface;

        Opened += (_, _) =>
        {
            ConfigureVirtualScreenBounds();
            Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _requestGlobalCancel?.Invoke();
            }
        };
        _selectionSurface.SelectionCompleted += (_, selection) => CompleteSelection(selection);
        _selectionSurface.SelectionCancelled += (_, _) => _requestGlobalCancel?.Invoke();
    }

    public static Task<ScreenSnipSelection?> ShowSelectionAsync(CancellationToken cancellationToken = default)
    {
        var probe = new ScreenSnipSelectionWindow(null, 1);
        var screens = probe.Screens.All;
        var windows = screens.Count == 0
            ? [probe]
            : screens.Select(static screen => new ScreenSnipSelectionWindow(screen.Bounds, screen.Scaling)).ToArray();
        if (screens.Count > 0)
        {
            probe.Close();
        }

        var completion = new TaskCompletionSource<ScreenSnipSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        var remainingWindows = windows.Length;
        CancellationTokenRegistration registration = default;

        void CancelAllWindows()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            registration.Dispose();
            completion.TrySetResult(null);
            foreach (var window in windows)
            {
                window.Close();
            }
        }

        foreach (var window in windows)
        {
            window._requestGlobalCancel = CancelAllWindows;
            window.Closed += (_, _) =>
            {
                remainingWindows--;
                if (!completed && window._selection is not null)
                {
                    completed = true;
                    registration.Dispose();
                    completion.TrySetResult(window._selection);
                    CloseRemainingWindows(windows, window);
                    return;
                }

                if (!completed && remainingWindows == 0)
                {
                    completed = true;
                    registration.Dispose();
                    completion.TrySetResult(null);
                }
            };
        }

        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(CancelAllWindows));
        }

        foreach (var window in windows)
        {
            window.Show();
        }

        return completion.Task;
    }

    private void ConfigureVirtualScreenBounds()
    {
        if (_screenBounds is { } screenBounds)
        {
            Position = new PixelPoint(screenBounds.X, screenBounds.Y);
            Width = Math.Max(1, screenBounds.Width / _screenScaling);
            Height = Math.Max(1, screenBounds.Height / _screenScaling);
            return;
        }

        var screens = Screens.All;
        if (screens.Count == 0)
        {
            WindowState = WindowState.FullScreen;
            return;
        }

        var left = screens.Min(static screen => screen.Bounds.X);
        var top = screens.Min(static screen => screen.Bounds.Y);
        var right = screens.Max(static screen => screen.Bounds.X + screen.Bounds.Width);
        var bottom = screens.Max(static screen => screen.Bounds.Y + screen.Bounds.Height);
        var renderScaling = RenderScaling <= 0 ? 1 : RenderScaling;

        Position = new PixelPoint(left, top);
        Width = Math.Max(1, (right - left) / renderScaling);
        Height = Math.Max(1, (bottom - top) / renderScaling);
    }

    private void CompleteSelection(Rect selection)
    {
        var origin = _screenBounds is { } screenBounds
            ? new PixelPoint(screenBounds.X, screenBounds.Y)
            : Position;
        var scaling = _screenBounds is null
            ? RenderScaling <= 0 ? 1 : RenderScaling
            : _screenScaling;
        var x = origin.X + (int)Math.Round(selection.X * scaling);
        var y = origin.Y + (int)Math.Round(selection.Y * scaling);
        var width = Math.Max(1, (int)Math.Round(selection.Width * scaling));
        var height = Math.Max(1, (int)Math.Round(selection.Height * scaling));

        _selection = new ScreenSnipSelection(x, y, width, height);
        Close();
    }

    private static void CloseRemainingWindows(
        IReadOnlyList<ScreenSnipSelectionWindow> windows,
        ScreenSnipSelectionWindow selectedWindow)
    {
        foreach (var window in windows)
        {
            if (!ReferenceEquals(window, selectedWindow))
            {
                window.Close();
            }
        }
    }

    private sealed class SelectionSurface : Control
    {
        private static readonly IBrush OverlayBrush = new SolidColorBrush(Color.FromArgb(122, 4, 7, 12));
        private static readonly IBrush SelectionFillBrush = new SolidColorBrush(Color.FromArgb(36, 173, 198, 255));
        private static readonly IPen SelectionBorderPen = new Pen(new SolidColorBrush(Color.Parse("#ADC6FF")), 2);
        private Point? _origin;
        private Rect? _selection;

        public event EventHandler<Rect>? SelectionCompleted;

        public event EventHandler? SelectionCancelled;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var point = e.GetPosition(this);
            _origin = point;
            _selection = new Rect(point, point);
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (_origin is null)
            {
                return;
            }

            _selection = NormalizeSelection(_origin.Value, e.GetPosition(this));
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_origin is null)
            {
                return;
            }

            var selection = NormalizeSelection(_origin.Value, e.GetPosition(this));
            _origin = null;
            _selection = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            InvalidateVisual();

            if (selection.Width < 4 || selection.Height < 4)
            {
                SelectionCancelled?.Invoke(this, EventArgs.Empty);
                return;
            }

            SelectionCompleted?.Invoke(this, selection);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            context.FillRectangle(OverlayBrush, Bounds);
            if (_selection is { } selection)
            {
                context.DrawRectangle(SelectionFillBrush, SelectionBorderPen, selection);
            }
        }

        private static Rect NormalizeSelection(Point start, Point end)
        {
            var x = Math.Min(start.X, end.X);
            var y = Math.Min(start.Y, end.Y);
            var width = Math.Abs(start.X - end.X);
            var height = Math.Abs(start.Y - end.Y);

            return new Rect(x, y, width, height);
        }
    }
}

public sealed record ScreenSnipSelection(int X, int Y, int Width, int Height);
