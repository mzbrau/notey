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

    [Fact]
    public void TryConvertToMarkdown_converts_word_tab_separated_bullets_to_list()
    {
        var text = "•\tFirst item\n•\tSecond item\n•\tThird item\n";

        var markdown = MarkdownClipboardFormatter.TryConvertToMarkdown(html: null, rtf: null, text, out var structuredHtmlDetected);

        Assert.False(structuredHtmlDetected);
        Assert.Equal("""
            - First item
            - Second item
            - Third item
            
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertToMarkdown_converts_word_html_list_layout_table_to_list()
    {
        var html = "<table><tr><td>•</td><td>Alpha</td></tr><tr><td>•</td><td>Beta</td></tr><tr><td>•</td><td>Gamma</td></tr></table>";

        var markdown = MarkdownClipboardFormatter.TryConvertToMarkdown(html, rtf: null, text: null, out var structuredHtmlDetected);

        Assert.True(structuredHtmlDetected);
        Assert.Equal("""
            - Alpha
            - Beta
            - Gamma
            
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertToMarkdown_converts_word_html_ordered_list_layout_table_to_list()
    {
        var html = "<table><tr><td>1.</td><td>First</td></tr><tr><td>2.</td><td>Second</td></tr><tr><td>3.</td><td>Third</td></tr></table>";

        var markdown = MarkdownClipboardFormatter.TryConvertToMarkdown(html, rtf: null, text: null, out var structuredHtmlDetected);

        Assert.True(structuredHtmlDetected);
        Assert.Equal("""
            1. First
            2. Second
            3. Third
            
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertToMarkdown_does_not_misidentify_real_html_table_as_list()
    {
        var html = "<table><tr><th>Name</th><th>Role</th></tr><tr><td>Alice</td><td>Engineer</td></tr><tr><td>Bob</td><td>Manager</td></tr></table>";

        var markdown = MarkdownClipboardFormatter.TryConvertToMarkdown(html, rtf: null, text: null, out var structuredHtmlDetected);

        Assert.True(structuredHtmlDetected);
        Assert.NotNull(markdown);
        Assert.Contains("|", markdown);
    }

    [Fact]
    public void TryConvertToMarkdown_converts_word_tab_separated_nested_bullets_to_nested_list()
    {
        var text = "•\tOn\n•\tRo\no\tSlkjf\n•\tstuf\n";

        var markdown = MarkdownClipboardFormatter.TryConvertToMarkdown(html: null, rtf: null, text, out var structuredHtmlDetected);

        Assert.False(structuredHtmlDetected);
        Assert.Equal("""
            - On
            - Ro
                - Slkjf
            - stuf
            
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertToMarkdown_converts_word_html_list_table_with_sub_bullets_to_nested_list()
    {
        var html = "<table><tr><td>•</td><td>On</td></tr><tr><td>•</td><td>Ro</td></tr><tr><td>o</td><td>Slkjf</td></tr><tr><td>•</td><td>stuf</td></tr></table>";

        var markdown = MarkdownClipboardFormatter.TryConvertToMarkdown(html, rtf: null, text: null, out var structuredHtmlDetected);

        Assert.True(structuredHtmlDetected);
        Assert.Equal("""
            - On
            - Ro
                - Slkjf
            - stuf
            
            """.ReplaceLineEndings("\n"), markdown);
    }
}
