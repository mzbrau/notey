namespace Notey.App.Editing.Spellcheck;

public interface ISpellcheckService
{
    bool IsAvailable { get; }

    bool IsCorrect(string word);

    IReadOnlyList<string> GetSuggestions(string word, int maxSuggestions);
}
