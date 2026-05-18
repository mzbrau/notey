namespace Notey.App.Imports;

public sealed record FileImportContext(string? DraftFilePath, string? FinalNotePath)
{
    public bool IsDraft => !string.IsNullOrWhiteSpace(DraftFilePath);

    public bool IsFinalNote => !string.IsNullOrWhiteSpace(FinalNotePath);

    public static FileImportContext ForDraft(string draftFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftFilePath);
        return new FileImportContext(draftFilePath, FinalNotePath: null);
    }

    public static FileImportContext ForFinalNote(string finalNotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalNotePath);
        return new FileImportContext(DraftFilePath: null, finalNotePath);
    }
}

public sealed record FileImportResult(string Markdown, IReadOnlyList<string> WrittenPaths);
