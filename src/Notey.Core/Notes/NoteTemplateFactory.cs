using System.Globalization;

namespace Notey.Core.Notes;

public sealed class NoteTemplateFactory
{
    public string Create(DateTimeOffset createdAt)
    {
        var timestamp = createdAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        var isoTimestamp = createdAt.ToString("O", CultureInfo.InvariantCulture);

        return $"""
            ---
            created: {isoTimestamp}
            people: []
            topics: []
            projects: []
            screenshots: []
            ---

            # Untitled note

            Captured: {timestamp}

            ## Notes

            """;
    }
}
