using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Notey.App.Views;
using Notey.Core.Platform;

namespace Notey.App.Platform;

public sealed class WindowsGlobalHotkeyService(
    MainWindow mainWindow,
    ILogger<WindowsGlobalHotkeyService> logger) : IGlobalHotkeyService, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly Dictionary<int, GlobalHotkeyRegistration> _registrations = [];
    private Win32Properties.CustomWndProcHookCallback? _wndProcHookCallback;
    private int _nextHotkeyId = 0x4E00;

    public ValueTask RegisterAsync(GlobalHotkeyRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            logger.LogInformation("Global hotkey {Gesture} for {Name} is unavailable on this platform.", registration.Gesture, registration.Name);
            return ValueTask.CompletedTask;
        }

        return new ValueTask(RegisterOnUiThreadAsync(registration));
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = mainWindow.TryGetPlatformHandle();
        if (handle is null || handle.HandleDescriptor != "HWND")
        {
            return;
        }

        foreach (var hotkeyId in _registrations.Keys.ToArray())
        {
            _ = UnregisterHotKey(handle.Handle, hotkeyId);
            _registrations.Remove(hotkeyId);
        }
    }

    public ValueTask UnregisterAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(UnregisterAllOnUiThreadAsync());
    }

    private void RegisterOnUiThread(GlobalHotkeyRegistration registration)
    {
        var handle = mainWindow.TryGetPlatformHandle();
        if (handle is null || handle.HandleDescriptor != "HWND")
        {
            throw new InvalidOperationException("Global hotkey registration requires a Windows HWND platform handle.");
        }

        var gesture = HotkeyGesture.Parse(registration.Gesture);
        var hotkeyId = _nextHotkeyId++;
        var modifiers = ToWin32Modifiers(gesture.Modifiers) | ModNoRepeat;
        var virtualKey = ToVirtualKey(gesture.Key);

        EnsureWndProcHook();

        if (!RegisterHotKey(handle.Handle, hotkeyId, modifiers, virtualKey))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Unable to register global hotkey '{registration.Gesture}'.");
        }

        _registrations.Add(hotkeyId, registration);
        logger.LogInformation("Registered global hotkey {Gesture} for {Name}.", registration.Gesture, registration.Name);
    }

    private async Task RegisterOnUiThreadAsync(GlobalHotkeyRegistration registration)
    {
        await Dispatcher.UIThread.InvokeAsync(() => RegisterOnUiThread(registration));
    }

    private async Task UnregisterAllOnUiThreadAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var handle = mainWindow.TryGetPlatformHandle();
            if (handle is null || handle.HandleDescriptor != "HWND")
            {
                return;
            }

            foreach (var hotkeyId in _registrations.Keys.ToArray())
            {
                _ = UnregisterHotKey(handle.Handle, hotkeyId);
                _registrations.Remove(hotkeyId);
            }
        });
    }

    private void EnsureWndProcHook()
    {
        if (_wndProcHookCallback is not null)
        {
            return;
        }

        _wndProcHookCallback = WndProcHook;
        Win32Properties.AddWndProcHookCallback(mainWindow, _wndProcHookCallback);
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || !_registrations.TryGetValue(wParam.ToInt32(), out var registration))
        {
            return IntPtr.Zero;
        }

        handled = true;
        _ = InvokeRegistrationAsync(registration);
        return IntPtr.Zero;
    }

    private async Task InvokeRegistrationAsync(GlobalHotkeyRegistration registration)
    {
        try
        {
            await registration.ActivatedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Global hotkey {Gesture} for {Name} failed.", registration.Gesture, registration.Name);
        }
    }

    private static uint ToWin32Modifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if ((modifiers & HotkeyModifiers.Alt) == HotkeyModifiers.Alt)
        {
            result |= ModAlt;
        }

        if ((modifiers & HotkeyModifiers.Control) == HotkeyModifiers.Control)
        {
            result |= ModControl;
        }

        if ((modifiers & HotkeyModifiers.Shift) == HotkeyModifiers.Shift)
        {
            result |= ModShift;
        }

        if ((modifiers & HotkeyModifiers.Windows) == HotkeyModifiers.Windows)
        {
            result |= ModWin;
        }

        return result;
    }

    private static uint ToVirtualKey(string key)
    {
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            return char.ToUpperInvariant(key[0]);
        }

        if (key.Length is >= 2 and <= 3
            && key[0] == 'F'
            && int.TryParse(key[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            return (uint)(0x70 + functionKey - 1);
        }

        return key switch
        {
            "BACKSPACE" => 0x08,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "END" => 0x23,
            "HOME" => 0x24,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            _ => throw new FormatException($"Unsupported global hotkey key '{key}'.")
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
