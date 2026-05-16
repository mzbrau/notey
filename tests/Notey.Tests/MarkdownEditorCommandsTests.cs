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
    public void ToggleBold_unwraps_selected_text()
    {
        var edit = MarkdownEditorCommands.ToggleBold("**hello**", 0, 9);

        Assert.Equal(new MarkdownTextEdit(0, 9, "hello", 0, 5, 5), edit);
    }

    [Fact]
    public void ToggleBold_inserts_markers_at_empty_selection()
    {
        var edit = MarkdownEditorCommands.ToggleBold("hello", 5, 0);

        Assert.Equal(new MarkdownTextEdit(5, 0, "****", 7, 0, 7), edit);
    }

    [Fact]
    public void ToggleItalic_wraps_selected_text()
    {
        var edit = MarkdownEditorCommands.ToggleItalic("hello world", 6, 5);

        Assert.Equal(new MarkdownTextEdit(6, 5, "_world_", 7, 5, 12), edit);
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

    [Fact]
    public void TryConvertPlainTextTable_converts_tab_delimited_table()
    {
        var converted = MarkdownTableFormatter.TryConvertPlainTextTable("Name\tRole\nAda\tEngineer", out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            | Name | Role     |
            | ---- | -------- |
            | Ada  | Engineer |
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertPlainTextTable_converts_google_docs_newline_cell_stream()
    {
        var text = """
            Name
            Description
            Thing
            Smell
            Taste
            John
            Big
            Stuff
            bad
            bad
            """.ReplaceLineEndings("\n");

        var converted = MarkdownTableFormatter.TryConvertPlainTextTable(text, out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            | Name | Description | Thing | Smell | Taste |
            | ---- | ----------- | ----- | ----- | ----- |
            | John | Big         | Stuff | bad   | bad   |
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertPlainTextTable_ignores_trailing_blank_google_docs_cells()
    {
        var text = """
            Name
            Description
            Thing
            Smell
            Taste
            John
            Big
            Stuff
            bad
            bad




            """.ReplaceLineEndings("\n");

        var converted = MarkdownTableFormatter.TryConvertPlainTextTable(text, out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            | Name | Description | Thing | Smell | Taste |
            | ---- | ----------- | ----- | ----- | ----- |
            | John | Big         | Stuff | bad   | bad   |
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertPlainTextTable_converts_business_headers()
    {
        var text = """
            Customer
            Project
            Owner
            Status
            Acme
            Migration
            Ada
            Open
            """.ReplaceLineEndings("\n");

        var converted = MarkdownTableFormatter.TryConvertPlainTextTable(text, out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            | Customer | Project   | Owner | Status |
            | -------- | --------- | ----- | ------ |
            | Acme     | Migration | Ada   | Open   |
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertPlainTextTable_converts_personal_headers()
    {
        var text = """
            Name
            Phone
            Birthday
            Relationship
            Alice
            555-0100
            1990-04-08
            Sibling
            """.ReplaceLineEndings("\n");

        var converted = MarkdownTableFormatter.TryConvertPlainTextTable(text, out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            | Name  | Phone    | Birthday   | Relationship |
            | ----- | -------- | ---------- | ------------ |
            | Alice | 555-0100 | 1990-04-08 | Sibling      |
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertPlainTextTable_converts_emergency_service_headers()
    {
        var text = """
            Incident
            Unit
            Call Type
            Location
            12345
            E21
            Medical
            High Street
            """.ReplaceLineEndings("\n");

        var converted = MarkdownTableFormatter.TryConvertPlainTextTable(text, out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            | Incident | Unit | Call Type | Location    |
            | -------- | ---- | --------- | ----------- |
            | 12345    | E21  | Medical   | High Street |
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertPlainTextTable_does_not_convert_ordinary_newline_list()
    {
        var text = """
            Buy milk
            Call John
            Pay bills
            Walk dog
            Review PR
            Book flight
            """.ReplaceLineEndings("\n");

        var converted = MarkdownTableFormatter.TryConvertPlainTextTable(text, out _);

        Assert.False(converted);
    }

    [Fact]
    public void TryConvertHtmlTable_converts_table_fragment()
    {
        var converted = MarkdownTableFormatter.TryConvertHtmlTable(
            "<table><tr><th>Name</th><th>Role</th></tr><tr><td>Ada</td><td>Engineer<br>Lead</td></tr></table>",
            out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            | Name | Role          |
            | ---- | ------------- |
            | Ada  | Engineer Lead |
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryConvertHtmlTable_rejects_merged_cells()
    {
        var converted = MarkdownTableFormatter.TryConvertHtmlTable(
            "<table><tr><td colspan=\"2\">Merged</td></tr></table>",
            out _);

        Assert.False(converted);
        Assert.True(MarkdownTableFormatter.ContainsHtmlTable("<table><tr><td colspan=\"2\">Merged</td></tr></table>"));
    }

    [Fact]
    public void TryConvertRtfTable_converts_cell_rows()
    {
        var rtf = @"{\rtf1\ansi Name\cell Description\cell\row John\cell Big\cell\row}";

        var converted = MarkdownTableFormatter.TryConvertRtfTable(rtf, out var markdown);

        Assert.True(converted);
        Assert.Equal("""
            | Name | Description |
            | ---- | ----------- |
            | John | Big         |
            """.ReplaceLineEndings("\n"), markdown);
    }

    [Fact]
    public void TryFormatTables_aligns_markdown_tables_and_preserves_alignment_markers()
    {
        var text = """
            |Name|Role|
            |:---|---:|
            |Ada|Engineer|
            """.ReplaceLineEndings("\n");

        var edit = MarkdownTableFormatter.TryFormatTables(text, text.Length);

        Assert.NotNull(edit);
        Assert.Equal("""
            | Name | Role     |
            | :--- | -------: |
            | Ada  | Engineer |
            """.ReplaceLineEndings("\n"), edit.ReplacementText);
    }

    [Fact]
    public void TryFormatTables_aligns_reported_sample_table()
    {
        var text = """
            | Name | Description | Thing | Smell | Taste |
            | ---- | ----------- | ----- | ----- | ----- |
            | John | Big | Stuff | bad | bad |
            | bla | dkj stuff | dkfjslkj | sdlkfj | sldkf |
            | lkjl | ljk | lk | kj | jlklkj |
            | | kjhkjhkj | | | |
            | | | | | |
            """.ReplaceLineEndings("\n");

        var edit = MarkdownTableFormatter.TryFormatTables(text, text.Length);

        Assert.NotNull(edit);
        Assert.Equal("""
            | Name | Description | Thing    | Smell  | Taste  |
            | ---- | ----------- | -------- | ------ | ------ |
            | John | Big         | Stuff    | bad    | bad    |
            | bla  | dkj stuff   | dkfjslkj | sdlkfj | sldkf  |
            | lkjl | ljk         | lk       | kj     | jlklkj |
            |      | kjhkjhkj    |          |        |        |
            |      |             |          |        |        |
            """.ReplaceLineEndings("\n"), edit.ReplacementText);
    }

    [Fact]
    public void TryFormatTables_ignores_fenced_code_blocks()
    {
        var text = """
            ```
            |Name|Role|
            |---|---|
            ```

            |Name|Role|
            |---|---|
            |Ada|Engineer|
            """.ReplaceLineEndings("\n");

        var edit = MarkdownTableFormatter.TryFormatTables(text, text.Length);

        Assert.NotNull(edit);
        Assert.Contains("|Name|Role|", edit.ReplacementText, StringComparison.Ordinal);
        Assert.Contains("| Name | Role     |", edit.ReplacementText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFormatTables_caps_column_padding_without_changing_cell_text()
    {
        var longValue = new string('x', 48);
        var text = $"""
            | Name | Description |
            | --- | --- |
            | Ada | {longValue} |
            | Bo | ok |
            """.ReplaceLineEndings("\n");

        var edit = MarkdownTableFormatter.TryFormatTables(text, text.Length);

        Assert.NotNull(edit);
        Assert.Contains(longValue, edit.ReplacementText, StringComparison.Ordinal);
        Assert.Contains($"| ---- | {new string('-', 40)} |", edit.ReplacementText, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('-', 48), edit.ReplacementText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryNavigateTableCell_moves_forward_between_cells_and_skips_separator()
    {
        var text = """
            | A | B |
            | --- | --- |
            | 1 | 2 |
            """.ReplaceLineEndings("\n");
        var caretOffset = text.IndexOf("B", StringComparison.Ordinal);
        var targetOffset = text.IndexOf("1", StringComparison.Ordinal);

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            caretOffset,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward);

        Assert.Equal(new MarkdownTextEdit(caretOffset, 0, string.Empty, targetOffset, 0, targetOffset), edit);
    }

    [Fact]
    public void TryNavigateTableCell_moves_backward_between_cells_and_skips_separator()
    {
        var text = """
            | A | B |
            | --- | --- |
            | 1 | 2 |
            """.ReplaceLineEndings("\n");
        var caretOffset = text.IndexOf("1", StringComparison.Ordinal);
        var targetOffset = text.IndexOf("B", StringComparison.Ordinal);

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            caretOffset,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Backward);

        Assert.Equal(new MarkdownTextEdit(caretOffset, 0, string.Empty, targetOffset, 0, targetOffset), edit);
    }

    [Fact]
    public void TryNavigateTableCell_keeps_shift_tab_in_first_editable_cell()
    {
        var text = """
            | A | B |
            | --- | --- |
            | 1 | 2 |
            """.ReplaceLineEndings("\n");
        var caretOffset = text.IndexOf("A", StringComparison.Ordinal);

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            caretOffset,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Backward);

        Assert.Equal(new MarkdownTextEdit(caretOffset, 0, string.Empty, caretOffset, 0, caretOffset), edit);
    }

    [Fact]
    public void TryNavigateTableCell_adds_row_from_last_cell_and_reformats_table()
    {
        var text = """
            | Name | Description |
            | --- | --- |
            | Ada | Engineer |
            """.ReplaceLineEndings("\n");
        var caretOffset = text.IndexOf("Engineer", StringComparison.Ordinal) + "Engineer".Length;

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            caretOffset,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward);

        Assert.NotNull(edit);
        Assert.Equal(0, edit.ReplacementStart);
        Assert.Equal(text.TrimEnd('\n').Length, edit.ReplacementLength);
        Assert.Equal("""
            | Name | Description |
            | ---- | ----------- |
            | Ada  | Engineer    |
            |      |             |
            """.ReplaceLineEndings("\n").TrimEnd('\n'), edit.ReplacementText);
        Assert.Equal(edit.ReplacementText.LastIndexOf("|      |", StringComparison.Ordinal) + 2, edit.CaretOffset);
    }

    [Fact]
    public void TryNavigateTableCell_adds_row_when_caret_is_after_last_column()
    {
        var text = """
            | A | B |
            | --- | --- |
            | 1 | 2 |
            """.ReplaceLineEndings("\n");
        var caretOffset = text.IndexOf("| 1 | 2 |", StringComparison.Ordinal) + "| 1 | 2 |".Length;

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            caretOffset,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward);

        Assert.NotNull(edit);
        Assert.Contains("|     |     |", edit.ReplacementText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryNavigateTableCell_moves_forward_without_rewriting_formatted_table()
    {
        var text = """
            | Name | Description | Thing    | Smell  | Taste  |
            | ---- | ----------- | -------- | ------ | ------ |
            | John | Big         | Stuff    | bad    | bad    |
            | bla  | dkj stuff   | dkfjslkj | sdlkfj | sldkf  |
            """.ReplaceLineEndings("\n");
        var caretOffset = text.IndexOf("John", StringComparison.Ordinal);
        var targetOffset = text.IndexOf("Big", StringComparison.Ordinal);

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            caretOffset,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward);

        Assert.Equal(new MarkdownTextEdit(caretOffset, 0, string.Empty, targetOffset, 0, targetOffset), edit);
    }

    [Fact]
    public void TryNavigateTableCell_adds_row_from_empty_final_row()
    {
        var text = """
            | A | B |
            | --- | --- |
            |   |   |
            """.ReplaceLineEndings("\n");
        var caretOffset = text.LastIndexOf("|", StringComparison.Ordinal) - 1;

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            caretOffset,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward);

        Assert.NotNull(edit);
        Assert.Equal(4, edit.ReplacementText.Split('\n').Length);
    }

    [Fact]
    public void TryNavigateTableCell_returns_null_for_selection_plain_text_and_fenced_table()
    {
        Assert.Null(MarkdownTableFormatter.TryNavigateTableCell(
            "| A | B |\n| --- | --- |\n| 1 | 2 |",
            caretOffset: 2,
            selectionLength: 1,
            MarkdownTableNavigationDirection.Forward));

        Assert.Null(MarkdownTableFormatter.TryNavigateTableCell(
            "plain text",
            caretOffset: 2,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward));

        var fenced = "```\n| A | B |\n| --- | --- |\n| 1 | 2 |\n```";
        Assert.Null(MarkdownTableFormatter.TryNavigateTableCell(
            fenced,
            fenced.IndexOf("1", StringComparison.Ordinal),
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward));
    }

    [Fact]
    public void TryNavigateTableCell_returns_null_on_line_after_table()
    {
        var text = """
            | A | B |
            | --- | --- |
            | 1 | 2 |

            """.ReplaceLineEndings("\n");

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            text.Length,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward);

        Assert.Null(edit);
    }

    [Fact]
    public void TryNavigateTableCell_handles_escaped_pipe_cells()
    {
        var text = """
            | A | B |
            | --- | --- |
            | x\|y | z |
            """.ReplaceLineEndings("\n");
        var caretOffset = text.IndexOf("x", StringComparison.Ordinal);
        var targetOffset = text.IndexOf("z", StringComparison.Ordinal);

        var edit = MarkdownTableFormatter.TryNavigateTableCell(
            text,
            caretOffset,
            selectionLength: 0,
            MarkdownTableNavigationDirection.Forward);

        Assert.Equal(new MarkdownTextEdit(caretOffset, 0, string.Empty, targetOffset, 0, targetOffset), edit);
    }
}
