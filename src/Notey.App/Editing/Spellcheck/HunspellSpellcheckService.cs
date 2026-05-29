using Microsoft.Extensions.Logging;
using WeCantSpell.Hunspell;

namespace Notey.App.Editing.Spellcheck;

public sealed class HunspellSpellcheckService : ISpellcheckService
{
    private const string DefaultLanguage = "en-US";
    private readonly string _dictionaryPath;
    private readonly string _affixPath;
    private readonly ILogger _logger;
    private readonly Lazy<WordList?> _wordList;

    public HunspellSpellcheckService(string dictionaryPath, string affixPath, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(affixPath);
        ArgumentNullException.ThrowIfNull(logger);

        _dictionaryPath = dictionaryPath;
        _affixPath = affixPath;
        _logger = logger;
        _wordList = new Lazy<WordList?>(LoadWordList, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => TryGetWordList() is not null;

    public static HunspellSpellcheckService CreateDefault(ILogger logger)
    {
        var dictionaryDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Dictionaries", DefaultLanguage);
        return new HunspellSpellcheckService(
            Path.Combine(dictionaryDirectory, "en_US.dic"),
            Path.Combine(dictionaryDirectory, "en_US.aff"),
            logger);
    }

    public bool IsCorrect(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        var normalizedWord = word.Trim();
        if (normalizedWord.Length == 0)
        {
            return true;
        }

        return TryGetWordList()?.Check(normalizedWord) ?? true;
    }

    public IReadOnlyList<string> GetSuggestions(string word, int maxSuggestions)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (maxSuggestions <= 0)
        {
            return [];
        }

        var normalizedWord = word.Trim();
        if (normalizedWord.Length == 0)
        {
            return [];
        }

        return TryGetWordList()
            ?.Suggest(normalizedWord)
            .Where(suggestion => !string.Equals(suggestion, normalizedWord, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxSuggestions)
            .ToArray()
            ?? [];
    }

    private WordList? TryGetWordList()
    {
        return _wordList.Value;
    }

    private WordList? LoadWordList()
    {
        if (!File.Exists(_dictionaryPath) || !File.Exists(_affixPath))
        {
            _logger.LogWarning(
                "Spellcheck dictionary files were not found. Dictionary: {DictionaryPath}; affix: {AffixPath}",
                _dictionaryPath,
                _affixPath);
            return null;
        }

        try
        {
            return WordList.CreateFromFiles(_dictionaryPath, _affixPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            _logger.LogError(ex, "Failed to load spellcheck dictionary from {DictionaryPath}.", _dictionaryPath);
            return null;
        }
    }
}
