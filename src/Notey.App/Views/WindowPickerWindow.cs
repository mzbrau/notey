using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Notey.App.Views;

public sealed class WindowPickerWindow : Window
{
    private readonly PickerSurface _surface = new();
    private readonly PixelRect? _screenBounds;
    private readonly double _screenScaling;
    private ScreenSnipSelection? _selection;
    private Action? _requestGlobalCancel;
    private Func<PixelPoint, ScreenSnipSelection?>? _resolveWindowAtPoint;

    private WindowPickerWindow(PixelRect? screenBounds, double screenScaling)
    {
        _screenBounds = screenBounds;
        _screenScaling = screenScaling <= 0 ? 1 : screenScaling;

        Title = "Select a window";
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Content = _surface;

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

        _surface.WindowHovered += (_, point) =>
        {
            var screenPoint = ToScreenPoint(point);
            var highlight = _resolveWindowAtPoint?.Invoke(screenPoint);
            _surface.SetHighlight(ToLocalRect(highlight));
        };
        _surface.WindowSelected += (_, point) =>
        {
            var screenPoint = ToScreenPoint(point);
            var selected = _resolveWindowAtPoint?.Invoke(screenPoint);
            if (selected is null || selected.Width < 4 || selected.Height < 4)
            {
                _requestGlobalCancel?.Invoke();
                return;
            }

            _selection = selected;
            Close();
        };
        _surface.SelectionCancelled += (_, _) => _requestGlobalCancel?.Invoke();
    }

    public static Task<ScreenSnipSelection?> ShowSelectionAsync(CancellationToken cancellationToken = default)
    {
        var windowsCatalog = EnumerateCaptureTargets();
        var probe = new WindowPickerWindow(null, 1);
        var screens = probe.Screens.All;
        var overlays = screens.Count == 0
            ? [probe]
            : screens.Select(static screen => new WindowPickerWindow(screen.Bounds, screen.Scaling)).ToArray();
        if (screens.Count > 0)
        {
            probe.Close();
        }

        var completion = new TaskCompletionSource<ScreenSnipSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        var remainingWindows = overlays.Length;
        CancellationTokenRegistration registration = default;

        ScreenSnipSelection? ResolveWindowAtPoint(PixelPoint point)
        {
            foreach (var candidate in windowsCatalog)
            {
                if (point.X >= candidate.X
                    && point.Y >= candidate.Y
                    && point.X < candidate.X + candidate.Width
                    && point.Y < candidate.Y + candidate.Height)
                {
                    return candidate;
                }
            }

            return null;
        }

        void CancelAllWindows()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            registration.Dispose();
            completion.TrySetResult(null);
            foreach (var window in overlays)
            {
                window.Close();
            }
        }

        foreach (var window in overlays)
        {
            window._requestGlobalCancel = CancelAllWindows;
            window._resolveWindowAtPoint = ResolveWindowAtPoint;
            window.Closed += (_, _) =>
            {
                remainingWindows--;
                if (!completed && window._selection is not null)
                {
                    completed = true;
                    registration.Dispose();
                    completion.TrySetResult(window._selection);
                    CloseRemainingWindows(overlays, window);
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

        foreach (var window in overlays)
        {
            window.Show();
        }

        return completion.Task;
    }

    private PixelPoint ToScreenPoint(Point localPoint)
    {
        var origin = _screenBounds is { } screenBounds
            ? new PixelPoint(screenBounds.X, screenBounds.Y)
            : Position;
        var scaling = _screenBounds is null
            ? RenderScaling <= 0 ? 1 : RenderScaling
            : _screenScaling;
        return new PixelPoint(
            origin.X + (int)Math.Round(localPoint.X * scaling),
            origin.Y + (int)Math.Round(localPoint.Y * scaling));
    }

    private Rect? ToLocalRect(ScreenSnipSelection? selection)
    {
        if (selection is null)
        {
            return null;
        }

        var origin = _screenBounds is { } screenBounds
            ? new PixelPoint(screenBounds.X, screenBounds.Y)
            : Position;
        var scaling = _screenBounds is null
            ? RenderScaling <= 0 ? 1 : RenderScaling
            : _screenScaling;

        return new Rect(
            (selection.X - origin.X) / scaling,
            (selection.Y - origin.Y) / scaling,
            selection.Width / scaling,
            selection.Height / scaling);
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

    private static void CloseRemainingWindows(
        IReadOnlyList<WindowPickerWindow> windows,
        WindowPickerWindow selectedWindow)
    {
        foreach (var window in windows)
        {
            if (!ReferenceEquals(window, selectedWindow))
            {
                window.Close();
            }
        }
    }

    private static List<ScreenSnipSelection> EnumerateCaptureTargets()
    {
        var results = new List<ScreenSnipSelection>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero)
            {
                return true;
            }

            var style = GetWindowLong(hwnd, GwlStyle);
            if ((style & WsChild) != 0 || (style & WsMinimize) != 0)
            {
                return true;
            }

            var exStyle = GetWindowLong(hwnd, GwlExStyle);
            if ((exStyle & WsExToolWindow) != 0)
            {
                return true;
            }

            NativeRect rect;
            if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<NativeRect>()) != 0
                && !GetWindowRect(hwnd, out rect))
            {
                return true;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 8 || height < 8)
            {
                return true;
            }

            results.Add(new ScreenSnipSelection(rect.Left, rect.Top, width, height));
            return true;
        }, IntPtr.Zero);

        // Prefer smaller (more specific) windows when hit-testing: sort by area ascending.
        results.Sort(static (left, right) => (left.Width * left.Height).CompareTo(right.Width * right.Height));
        return results;
    }

    private sealed class PickerSurface : Control
    {
        private static readonly IBrush OverlayBrush = new SolidColorBrush(Color.FromArgb(122, 4, 7, 12));
        private static readonly IBrush HighlightFillBrush = new SolidColorBrush(Color.FromArgb(48, 173, 198, 255));
        private static readonly IPen HighlightBorderPen = new Pen(new SolidColorBrush(Color.Parse("#ADC6FF")), 2);
        private Rect? _highlight;

        public event EventHandler<Point>? WindowHovered;

        public event EventHandler<Point>? WindowSelected;

        public event EventHandler? SelectionCancelled;

        public void SetHighlight(Rect? highlight)
        {
            _highlight = highlight;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            WindowHovered?.Invoke(this, e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            WindowSelected?.Invoke(this, e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                SelectionCancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(OverlayBrush, Bounds);
            if (_highlight is { } highlight)
            {
                context.DrawRectangle(HighlightFillBrush, HighlightBorderPen, highlight);
            }
        }
    }

    private const int GwOwner = 4;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsChild = 0x40000000;
    private const int WsMinimize = 0x20000000;
    private const int WsExToolWindow = 0x00000080;
    private const int DwmwaExtendedFrameBounds = 9;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect rect, int size);
}
