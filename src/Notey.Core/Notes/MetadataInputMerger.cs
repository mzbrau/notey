namespace Notey.Core.Notes;

public static class MetadataInputMerger
{
    public static string Merge(
        string currentInput,
        IEnumerable<string> additions,
        Func<string, string> normalizer)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(normalizer);

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddValues(SplitCurrentInput(currentInput), values, seen, normalizer);
        AddValues(additions, values, seen, normalizer);

        return string.Join(", ", values);
    }

    private static void AddValues(
        IEnumerable<string> candidates,
        ICollection<string> values,
        ISet<string> seen,
        Func<string, string> normalizer)
    {
        foreach (var candidate in candidates)
        {
            var normalized = normalizer(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                continue;
            }

            values.Add(normalized);
        }
    }

    private static IEnumerable<string> SplitCurrentInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
