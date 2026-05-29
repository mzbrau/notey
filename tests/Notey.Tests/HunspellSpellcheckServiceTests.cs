using Microsoft.Extensions.Logging.Abstractions;
using Notey.App.Editing.Spellcheck;

namespace Notey.Tests;

public sealed class HunspellSpellcheckServiceTests
{
    [Fact]
    public void Bundled_en_us_dictionary_checks_words_and_returns_suggestions()
    {
        var service = HunspellSpellcheckService.CreateDefault(NullLogger.Instance);

        Assert.True(service.IsAvailable);
        Assert.True(service.IsCorrect("spelling"));
        Assert.False(service.IsCorrect("speling"));
        Assert.Contains("spelling", service.GetSuggestions("speling", 5));
    }
}
