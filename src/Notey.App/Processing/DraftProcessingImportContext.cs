namespace Notey.App.Processing;

public sealed record DraftProcessingImportContext(
    string SourceFileName,
    string SourceRelativePath,
    DateTimeOffset? SourceModifiedAt = null)
{
    public string ToPromptText()
    {
        var modified = SourceModifiedAt is null
            ? "unknown"
            : SourceModifiedAt.Value.ToString("yyyy-MM-dd HH:mm zzz", System.Globalization.CultureInfo.InvariantCulture);
        return $"""
            - source file name: {SourceFileName}
            - source relative path: {SourceRelativePath}
            - source modified at: {modified}
            """;
    }
}
