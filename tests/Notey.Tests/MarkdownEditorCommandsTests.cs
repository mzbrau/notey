using Notey.Core.Notes;

namespace Notey.Tests;

public sealed class MarkdownEditorCommandsTests
{
    [Fact]
    public void ToggleBold_wraps_selected_text()
    {
        var edit = MarkdownEditorCommands.ToggleBold("hello world", 6, 5);

        Assert.Equal(new MarkdownTextEdit(6, 5, "**world**", 8, 5, 13), edit);
    }

    [Fact]
    public void ToggleItalic_unwraps_selection_with_surrounding_markers()
    {
        var edit = MarkdownEditorCommands.ToggleItalic("hello _world_", 7, 5);

        Assert.Equal(new MarkdownTextEdit(6, 7, "world", 6, 5, 11), edit);
    }

    [Fact]
    public void TryCreateListContinuation_continues_unordered_list()
    {
        var text = "- first item";

        var edit = MarkdownEditorCommands.TryCreateListContinuation(text, text.Length);

        Assert.Equal(new MarkdownTextEdit(12, 0, "\n- ", 15, 0, 15), edit);
    }

    [Fact]
    public void TryCreateListContinuation_increments_ordered_list()
    {
        var text = "9. ninth item";

        var edit = MarkdownEditorCommands.TryCreateListContinuation(text, text.Length);

        Assert.Equal(new MarkdownTextEdit(13, 0, "\n10. ", 18, 0, 18), edit);
    }

    [Fact]
    public void TryCreateListContinuation_ignores_ordered_numbers_that_cannot_increment()
    {
        var text = "999999999999999999999999. item";

        var edit = MarkdownEditorCommands.TryCreateListContinuation(text, text.Length);

        Assert.Null(edit);
    }

    [Fact]
    public void TryCreateListContinuation_ignores_caret_at_document_start()
    {
        var edit = MarkdownEditorCommands.TryCreateListContinuation("\n- item", 0);

        Assert.Null(edit);
    }

    [Fact]
    public void TryCreateListContinuation_reports_invalid_caret_parameter_name()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => MarkdownEditorCommands.TryCreateListContinuation("- item", 99));

        Assert.Equal("caretOffset", exception.ParamName);
    }
}
