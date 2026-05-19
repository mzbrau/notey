using Notey.Core.Notes;

namespace Notey.Tests;

public sealed class PersonDisplayNameFormatterTests
{
    [Theory]
    [InlineData("james simpson", "James Simpson")]
    [InlineData("  jane   doe  ", "Jane Doe")]
    [InlineData("MARY ANN SMITH", "Mary Ann Smith")]
    public void ToTitleCase_normalizes_person_names(string input, string expected)
    {
        Assert.Equal(expected, PersonDisplayNameFormatter.ToTitleCase(input));
    }

    [Fact]
    public void ToTitleCase_rejects_blank_names()
    {
        Assert.Throws<ArgumentException>(() => PersonDisplayNameFormatter.ToTitleCase("   "));
    }
}
