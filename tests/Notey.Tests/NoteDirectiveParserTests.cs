using Notey.Core.Notes;

namespace Notey.Tests;

public sealed class NoteDirectiveParserTests
{
    [Fact]
    public void Parse_strips_known_directive_lines_and_keeps_body()
    {
        var parsed = new NoteDirectiveParser().Parse("""
            /meeting

            /customer Microsoft
            /topic Accounts

            Keep the accounts safe.
            """, ["customer"]);

        Assert.True(parsed.IsMeeting);
        Assert.Equal("Accounts", parsed.Topic);
        var dynamic = Assert.Single(parsed.DynamicDirectives);
        Assert.Equal("customer", dynamic.CommandName);
        Assert.Equal("Microsoft", dynamic.Value);
        Assert.Equal("Keep the accounts safe.", parsed.Body);
    }

    [Fact]
    public void Parse_extracts_tasks_and_due_dates()
    {
        var parsed = new NoteDirectiveParser().Parse("""
            /task Send recap // 2026-05-20
            Notes body
            """, []);

        var task = Assert.Single(parsed.Tasks);
        Assert.Equal("Send recap", task.Text);
        Assert.Equal(new DateOnly(2026, 5, 20), task.DueDate);
        Assert.Equal("Notes body", parsed.Body);
    }

    [Fact]
    public void Parse_preserves_unknown_commands_in_body()
    {
        var parsed = new NoteDirectiveParser().Parse("""
            /unknown value
            Body
            """, []);

        Assert.Equal("/unknown value\nBody", parsed.Body);
        Assert.Equal("unknown", Assert.Single(parsed.UnknownDirectives).CommandName);
    }

    [Fact]
    public void SlashCommandCompletionQuery_matches_command_at_line_start()
    {
        var query = SlashCommandCompletionQuery.TryCreate("Hello\n  /top", 12);

        Assert.NotNull(query);
        Assert.Equal(8, query.ReplacementStart);
        Assert.Equal(4, query.ReplacementLength);
        Assert.Equal("top", query.SearchText);
    }

    [Fact]
    public void SlashCommandCompletionQuery_does_not_match_slash_inside_word()
    {
        var text = "Visit http://example.com";

        var query = SlashCommandCompletionQuery.TryCreate(text, text.Length);

        Assert.Null(query);
    }

    [Fact]
    public void SlashCommandCompletionQuery_ignores_document_start_before_leading_newline()
    {
        var query = SlashCommandCompletionQuery.TryCreate("\n/topic", 0);

        Assert.Null(query);
    }

    [Fact]
    public void SlashCommandParameterQuery_matches_parameter_after_command()
    {
        var query = SlashCommandParameterQuery.TryCreate("/topic Acc", 10);

        Assert.NotNull(query);
        Assert.Equal("topic", query.CommandName);
        Assert.Equal("Acc", query.SearchText);
        Assert.Equal(7, query.ReplacementStart);
        Assert.Equal(3, query.ReplacementLength);
    }

    [Fact]
    public void SlashCommandParameterQuery_ignores_document_start_before_leading_newline()
    {
        var query = SlashCommandParameterQuery.TryCreate("\n/topic Acc", 0);

        Assert.Null(query);
    }

    [Fact]
    public void SlashCommandParameterQuery_detects_task_due_date_trigger()
    {
        var query = SlashCommandParameterQuery.TryCreate("/task Send recap // ", 19);

        Assert.NotNull(query);
        Assert.True(query.IsTaskDueDateQuery);
    }
}
