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

    [Fact]
    public void TryBeginOpenRecentDialog_sets_guard_on_first_entry()
    {
        var isOpen = false;

        var opened = MainWindow.TryBeginOpenRecentDialog(ref isOpen);

        Assert.True(opened);
        Assert.True(isOpen);
    }

    [Fact]
    public void TryBeginOpenRecentDialog_rejects_reentry_when_already_open()
    {
        var isOpen = true;

        var opened = MainWindow.TryBeginOpenRecentDialog(ref isOpen);

        Assert.False(opened);
        Assert.True(isOpen);
    }
}
