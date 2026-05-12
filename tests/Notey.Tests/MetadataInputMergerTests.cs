using Notey.Core.Notes;

namespace Notey.Tests;

public sealed class MetadataInputMergerTests
{
    [Fact]
    public void Merge_preserves_existing_values_and_appends_unique_normalized_additions()
    {
        var result = MetadataInputMerger.Merge(
            "@Jane Doe, [[People/John Smith|John Smith]]",
            ["jane doe", " Alex   Rider ", ""],
            NormalizePerson);

        Assert.Equal("Jane Doe, John Smith, Alex Rider", result);
    }

    [Fact]
    public void Merge_handles_empty_current_input()
    {
        var result = MetadataInputMerger.Merge(
            string.Empty,
            ["#roadmap", "#Roadmap", "meeting"],
            static value => string.Join(' ', value.Trim().TrimStart('#').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

        Assert.Equal("roadmap, meeting", result);
    }

    private static string NormalizePerson(string value)
    {
        var trimmed = value.Trim().TrimStart('@').Trim();
        if (trimmed.StartsWith("[[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal))
        {
            var inner = trimmed[2..^2];
            var aliasSeparator = inner.LastIndexOf('|');
            if (aliasSeparator >= 0 && aliasSeparator + 1 < inner.Length)
            {
                return inner[(aliasSeparator + 1)..].Trim();
            }
        }

        return string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
