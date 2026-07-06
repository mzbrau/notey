using Avalonia.Input;
using Notey.App.Assistant;
using Notey.App.Views;
using Notey.Vault.Tasks;

namespace Notey.Tests;

public sealed class MainWindowShortcutTests
{
    [Theory]
    [InlineData(Key.R, KeyModifiers.Control, true)]
    [InlineData(Key.R, KeyModifiers.Meta, true)]
    [InlineData(Key.R, KeyModifiers.None, true)]
    [InlineData(Key.R, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(Key.N, KeyModifiers.Control, false)]
    public void IsOpenRecentDialogShortcut_matches_control_or_command_r(Key key, KeyModifiers modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindow.IsOpenRecentDialogShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.T, KeyModifiers.Control, false)]
    [InlineData(Key.T, KeyModifiers.Meta, false)]
    [InlineData(Key.T, KeyModifiers.None, false)]
    [InlineData(Key.T, KeyModifiers.Control | KeyModifiers.Alt, false)]
    [InlineData(Key.N, KeyModifiers.Control, false)]
    public void IsNewTaskShortcut_matches_control_or_command_t(Key key, KeyModifiers modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindow.IsNewTaskShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.T, KeyModifiers.Control | KeyModifiers.Alt, true)]
    [InlineData(Key.T, KeyModifiers.Meta | KeyModifiers.Alt, true)]
    [InlineData(Key.T, KeyModifiers.Control, false)]
    [InlineData(Key.T, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(Key.K, KeyModifiers.Control | KeyModifiers.Alt, false)]
    public void IsFormatTablesShortcut_matches_control_or_command_alt_t(Key key, KeyModifiers modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindow.IsFormatTablesShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Enter, KeyModifiers.None, true)]
    [InlineData(Key.Enter, KeyModifiers.Control, true)]
    [InlineData(Key.Enter, KeyModifiers.Meta, true)]
    [InlineData(Key.Enter, KeyModifiers.Shift, false)]
    [InlineData(Key.Enter, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(Key.Tab, KeyModifiers.None, false)]
    public void IsAssistantSendShortcut_matches_enter_without_shift_or_extra_modifiers(Key key, KeyModifiers modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindow.IsAssistantSendShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(1180, 96, 920)]
    [InlineData(820, 96, 700)]
    [InlineData(420, 96, 300)]
    public void CalculateCompletionPanelWidth_uses_available_editor_width(double editorWidth, double leftOffset, double expected)
    {
        Assert.Equal(expected, MainWindow.CalculateCompletionPanelWidth(editorWidth, leftOffset));
    }

    [Theory]
    [InlineData(1, 280, 58)]
    [InlineData(3, 280, 142)]
    [InlineData(10, 280, 280)]
    public void EstimateCompletionPanelHeight_uses_suggestion_count_before_flipping(int suggestionCount, double maxHeight, double expected)
    {
        Assert.Equal(expected, MainWindow.EstimateCompletionPanelHeight(suggestionCount, maxHeight));
    }

    [Theory]
    [InlineData("james simpson", "James Simpson")]
    [InlineData("iOS Team", "iOS Team")]
    [InlineData("Jane \"JJ\" Doe", "Jane \"JJ\" Doe")]
    public void GetPersonCompletionDisplayName_title_cases_lowercase_without_rewriting_intentional_casing(string input, string expected)
    {
        Assert.Equal(expected, MainWindow.GetPersonCompletionDisplayName(input));
    }

    [Fact]
    public void FormatAssistantResult_shows_full_replace_all_text()
    {
        var result = new NoteyAssistantResult(
            "I rewrote the note.",
            [new ReplaceAllNoteTextOperation("# Plan\n\n- first\n- second", string.Empty)],
            [],
            []);

        var formatted = MainWindow.FormatAssistantResult(result, Array.Empty<NoteyTask>()).ReplaceLineEndings("\n");

        Assert.Contains("I rewrote the note.", formatted, StringComparison.Ordinal);
        Assert.Contains("1. Replace entire note", formatted, StringComparison.Ordinal);
        Assert.Contains("     Proposed text:\n     ---\n     # Plan\n     \n     - first\n     - second\n     ---", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("Replace entire note with", formatted, StringComparison.Ordinal);
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
