using Notey.App.Editing.Spellcheck;

namespace Notey.Tests;

public sealed class MarkdownSpellcheckIndexTests
{
    [Fact]
    public void Build_reports_misspellings_in_plain_prose()
    {
        var spellcheck = FakeSpellcheckService.WithMisspellings("speling");

        var spans = MarkdownSpellcheckIndex.Build("This speling should be marked.", spellcheck);

        var span = Assert.Single(spans);
        Assert.Equal("speling", span.Word);
        Assert.Equal("This ".Length, span.Offset);
        Assert.Equal("speling".Length, span.Length);
        Assert.Same(span, MarkdownSpellcheckIndex.FindSpanAtOffset(spans, span.EndOffset - 1));
        Assert.Null(MarkdownSpellcheckIndex.FindSpanAtOffset(spans, span.EndOffset));
    }

    [Fact]
    public void Build_skips_markdown_non_prose_ranges()
    {
        const string text = """
            This speling should be marked.
            `speling`
            ```
            speling
            ```
            [[People/speling|Speling Person]]
            ![[Images/speling.png]]
            See [speling](https://example.com/speling), https://speling.example.com, test@speling.example, and #speling.
            /speling command
            """;
        var spellcheck = FakeSpellcheckService.WithMisspellings("speling");

        var spans = MarkdownSpellcheckIndex.Build(text, spellcheck);

        var span = Assert.Single(spans);
        Assert.Equal(text.IndexOf("speling", StringComparison.Ordinal), span.Offset);
    }

    [Fact]
    public void Build_returns_empty_when_dictionary_is_unavailable()
    {
        var spans = MarkdownSpellcheckIndex.Build("speling", new FakeSpellcheckService(false, new HashSet<string>()));

        Assert.Empty(spans);
    }

    private sealed class FakeSpellcheckService(bool isAvailable, IReadOnlySet<string> misspellings) : ISpellcheckService
    {
        public bool IsAvailable { get; } = isAvailable;

        public static FakeSpellcheckService WithMisspellings(params string[] misspellings)
        {
            return new FakeSpellcheckService(true, misspellings.ToHashSet(StringComparer.OrdinalIgnoreCase));
        }

        public bool IsCorrect(string word)
        {
            return !misspellings.Contains(word);
        }

        public IReadOnlyList<string> GetSuggestions(string word, int maxSuggestions)
        {
            return maxSuggestions <= 0 ? [] : [$"{word}-suggestion"];
        }
    }
}
