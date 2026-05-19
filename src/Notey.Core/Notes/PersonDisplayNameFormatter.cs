using System.Globalization;

namespace Notey.Core.Notes;

public static class PersonDisplayNameFormatter
{
    public static string ToTitleCase(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        var normalized = string.Join(' ', displayName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Person display name cannot be empty.", nameof(displayName));
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }
}
