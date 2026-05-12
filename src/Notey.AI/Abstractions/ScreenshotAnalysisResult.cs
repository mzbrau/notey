namespace Notey.AI.Abstractions;

public sealed record ScreenshotAnalysisResult(
    string Summary,
    string? MeetingTitle,
    IReadOnlyList<ExtractedEntity> Entities,
    string RawModelOutput);
