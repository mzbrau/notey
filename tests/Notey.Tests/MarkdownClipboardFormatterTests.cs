using Notey.Core.Notes;

namespace Notey.Tests;

public sealed class MarkdownClipboardFormatterTests
{
    [Fact]
    public void TryConvertHtmlList_converts_nested_unordered_list()
    {
        var converted = MarkdownClipboardFormatter.TryConvertHtmlList(
            "<ul><li>Parent<ul><li>Child</li><li>Second child</li></ul></li><li>Peer</li></ul>",
            out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            - Parent
                - Child
                - Second child
            - Peer
            
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertHtmlList_preserves_inline_markdown_and_checkbox_items()
    {
        var converted = MarkdownClipboardFormatter.TryConvertHtmlList(
            "<ul><li><input type=\"checkbox\" checked><strong>Bold</strong> <em>italic</em> <a href=\"https://example.com\">link</a></li></ul>",
            out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            - [x] **Bold** _italic_ [link](https://example.com)
            
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertHtmlList_preserves_ordered_list_start_and_nested_mixed_lists()
    {
        var converted = MarkdownClipboardFormatter.TryConvertHtmlList(
            "<ol start=\"5\"><li>Step<ul><li>Detail</li></ul></li><li>Finish</li></ol>",
            out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            5. Step
                - Detail
            6. Finish
            
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertHtmlList_ignores_lists_nested_inside_tables()
    {
        var html = "<table><tr><td><ul><li>Nested</li></ul></td></tr></table>";

        Assert.False(MarkdownClipboardFormatter.ContainsHtmlList(html));
        Assert.False(MarkdownClipboardFormatter.TryConvertHtmlList(html, out _));
    }

    [Fact]
    public void TryConvertPlainTextList_converts_nested_bullets_and_tasks()
    {
        var text = """
            • Parent
                ☐ Child task
                • Child bullet
            • Peer
            """.ReplaceLineEndings("\n");

        var converted = MarkdownClipboardFormatter.TryConvertPlainTextList(text, out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            - Parent
                - [ ] Child task
                - Child bullet
            - Peer
            
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertPlainTextList_does_not_convert_single_item_text()
    {
        var converted = MarkdownClipboardFormatter.TryConvertPlainTextList("- Only one item", out _);

        Assert.False(converted);
    }

    [Fact]
    public void TryConvertToMarkdown_does_not_fall_back_to_plain_text_when_structured_html_fails()
    {
        var html = "<table><tr><td colspan=\"2\">Merged</td></tr></table>";
        var text = """
            • One
            • Two
            """.ReplaceLineEndings("\n");

        var markdown = MarkdownClipboardFormatter.TryConvertToMarkdown(html, rtf: null, text, out var structuredHtmlDetected);

        Assert.True(structuredHtmlDetected);
        Assert.Null(markdown);
    }
}
