using Avalonia.Input;
using Notey.App.Views;

namespace Notey.Tests;

public sealed class MainWindowShortcutTests
{
    [Theory]
    [InlineData(Key.R, KeyModifiers.Control, true)]
    [InlineData(Key.R, KeyModifiers.Meta, true)]
    [InlineData(Key.R, KeyModifiers.None, false)]
    [InlineData(Key.R, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(Key.N, KeyModifiers.Control, false)]
    public void IsOpenRecentDialogShortcut_matches_control_or_command_r(Key key, KeyModifiers modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindow.IsOpenRecentDialogShortcut(key, modifiers));
    }
}
