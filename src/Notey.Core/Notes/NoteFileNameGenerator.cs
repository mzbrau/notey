namespace Notey.Core.Notes;

public sealed class NoteFileNameGenerator
{
    public string Generate(DateTimeOffset createdAt)
    {
        return $"{createdAt:yyyy-MM-dd-HHmm}-note.md";
    }
}
