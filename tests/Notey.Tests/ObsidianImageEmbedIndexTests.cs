using Notey.App.Editing;

namespace Notey.Tests;

public sealed class ObsidianImageEmbedIndexTests
{
    [Fact]
    public void Build_finds_image_embeds_on_content_lines()
    {
        var embeds = ObsidianImageEmbedIndex.Build("""
            # Note

            ![[Attachments/Snips/shot.png]]
            """);

        var embed = Assert.Single(embeds);
        Assert.Equal(3, embed.Key);
        Assert.Equal("Attachments/Snips/shot.png", embed.Value.VaultRelativePath);
    }

    [Fact]
    public void Build_ignores_embeds_inside_fenced_code_blocks()
    {
        var embeds = ObsidianImageEmbedIndex.Build("""
            ```md
            ![[Attachments/Snips/shot.png]]
            ```
            """);

        Assert.Empty(embeds);
    }

    [Fact]
    public void Build_ignores_embeds_inside_inline_code()
    {
        var embeds = ObsidianImageEmbedIndex.Build("Use `![[Attachments/Snips/shot.png]]` in docs.");

        Assert.Empty(embeds);
    }

    [Fact]
    public void Build_uses_first_image_embed_on_line_and_supports_aliases()
    {
        var embeds = ObsidianImageEmbedIndex.Build("![[Attachments/Snips/first.png|Preview]] and ![[Attachments/Snips/second.png]]");

        var embed = Assert.Single(embeds);
        Assert.Equal("Attachments/Snips/first.png", embed.Value.VaultRelativePath);
    }

    [Fact]
    public void Build_ignores_non_image_embeds()
    {
        var embeds = ObsidianImageEmbedIndex.Build("![[Notes/agenda.md]]");

        Assert.Empty(embeds);
    }
}
