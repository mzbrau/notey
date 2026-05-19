using Notey.App.Editing;

namespace Notey.Tests;

public sealed class PeopleWikiLinkIndexTests
{
    [Fact]
    public void Build_returns_people_link_alias_span()
    {
        const string text = "Met [[People/james simpson|James Simpson]] today.";

        var span = Assert.Single(PeopleWikiLinkIndex.Build(text));

        Assert.Equal(text.IndexOf("[[", StringComparison.Ordinal), span.Offset);
        Assert.Equal("[[People/james simpson|James Simpson]]".Length, span.Length);
        Assert.Equal("People/james simpson", span.LinkPath);
        Assert.Equal("James Simpson", span.DisplayText);
    }

    [Fact]
    public void Build_uses_people_path_file_name_without_alias()
    {
        var span = Assert.Single(PeopleWikiLinkIndex.Build("[[People/Jane Doe]]"));

        Assert.Equal("Jane Doe", span.DisplayText);
    }

    [Fact]
    public void Build_ignores_non_people_links_and_image_embeds()
    {
        var links = PeopleWikiLinkIndex.Build("See [[Notes/Topics/Planning|Planning]] and ![[People/Jane Doe.png]].");

        Assert.Empty(links);
    }

    [Fact]
    public void Build_ignores_links_in_fenced_code_and_inline_code()
    {
        var text = """
            ```
            [[People/Jane Doe|Jane Doe]]
            ```
            `[[People/John Doe|John Doe]]`
            ``[[People/Grace Hopper|Grace Hopper]]``
            [[People/Ada Lovelace|Ada Lovelace]]
            """.ReplaceLineEndings("\n");

        var span = Assert.Single(PeopleWikiLinkIndex.Build(text));

        Assert.Equal("Ada Lovelace", span.DisplayText);
    }

    [Fact]
    public void Build_continues_after_unclosed_wiki_link_on_same_line()
    {
        const string text = "Broken [[ link then [[People/Jane Doe|Jane Doe]]";

        var span = Assert.Single(PeopleWikiLinkIndex.Build(text));

        Assert.Equal("Jane Doe", span.DisplayText);
    }

    [Fact]
    public void Build_unescapes_alias_pipes()
    {
        var span = Assert.Single(PeopleWikiLinkIndex.Build(@"[[People/Jane Doe|Jane \| JD]]"));

        Assert.Equal("Jane | JD", span.DisplayText);
    }
}
