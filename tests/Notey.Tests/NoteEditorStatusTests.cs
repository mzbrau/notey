using Notey.Core.Notes;

namespace Notey.Tests;

public sealed class NoteEditorStatusTests
{
    [Fact]
    public void FromText_counts_words_and_cursor_position()
    {
        var text = "# Title" + Environment.NewLine + "Two linked words";

        var status = NoteEditorStatus.FromText(text, text.Length);

        Assert.Equal(new NoteEditorStatus(4, 2, 17), status);
    }
}
