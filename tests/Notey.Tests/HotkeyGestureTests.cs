using Notey.Core.Platform;

namespace Notey.Tests;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void Parse_normalizes_default_open_note_hotkey()
    {
        var gesture = HotkeyGesture.Parse("Ctrl+Alt+N");

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, gesture.Modifiers);
        Assert.Equal("N", gesture.Key);
    }

    [Fact]
    public void Parse_rejects_missing_modifier()
    {
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse("N"));
    }

    [Fact]
    public void Parse_rejects_duplicate_modifiers()
    {
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse("Ctrl+Control+N"));
    }

    [Fact]
    public void Parse_normalizes_key_aliases()
    {
        var gesture = HotkeyGesture.Parse("Command+Return");

        Assert.Equal(HotkeyModifiers.Windows, gesture.Modifiers);
        Assert.Equal("ENTER", gesture.Key);
    }
}
