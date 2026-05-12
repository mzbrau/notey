namespace Notey.Pipelines.Data;

public abstract record PipelineData
{
    public abstract PipelineDataType Type { get; }
}

public sealed record ImageData(
    string FilePath,
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    string MediaType = "image/png") : PipelineData
{
    public override PipelineDataType Type => PipelineDataType.ImageData;
}

public sealed record TextData(string Text, string? Source = null) : PipelineData
{
    public override PipelineDataType Type => PipelineDataType.TextData;
}

public sealed record OcrTextData(
    string Text,
    string? Source = null,
    string? Language = null,
    double? Confidence = null) : PipelineData
{
    public override PipelineDataType Type => PipelineDataType.OcrTextData;
}

public sealed record StructuredNoteData(
    string? Summary = null,
    string? MeetingTitle = null,
    IReadOnlyList<EntitySuggestion>? People = null,
    IReadOnlyList<EntitySuggestion>? Topics = null,
    IReadOnlyList<EntitySuggestion>? Projects = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, string>? Sections = null) : PipelineData
{
    public override PipelineDataType Type => PipelineDataType.StructuredNoteData;
}

public sealed record EntitySuggestion(
    string Name,
    string Kind,
    double? Confidence = null,
    string? Source = null);

public sealed record MarkdownContent(string Markdown, string? Source = null) : PipelineData
{
    public override PipelineDataType Type => PipelineDataType.MarkdownContent;
}
