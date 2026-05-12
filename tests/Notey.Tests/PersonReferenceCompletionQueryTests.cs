using Notey.Core.Notes;

namespace Notey.Tests;

public sealed class PersonReferenceCompletionQueryTests
{
    [Fact]
    public void TryCreate_returns_query_for_person_reference_on_current_line()
    {
        var query = PersonReferenceCompletionQuery.TryCreate("Talk to @Jane Do", 16);

        Assert.NotNull(query);
        Assert.Equal(8, query.ReplacementStart);
        Assert.Equal(8, query.ReplacementLength);
        Assert.Equal("Jane Do", query.SearchText);
    }

    [Fact]
    public void TryCreate_ignores_email_addresses()
    {
        var query = PersonReferenceCompletionQuery.TryCreate("me@example.com", 10);

        Assert.Null(query);
    }

    [Fact]
    public void TryCreate_ignores_previous_line_references()
    {
        var query = PersonReferenceCompletionQuery.TryCreate("@Jane\nnotes", 11);

        Assert.Null(query);
    }
}
