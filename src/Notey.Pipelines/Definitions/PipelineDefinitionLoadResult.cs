namespace Notey.Pipelines.Definitions;

public sealed record PipelineDefinitionLoadResult(
    IReadOnlyList<PipelineDefinition> Definitions,
    IReadOnlyList<string> Warnings)
{
    public static PipelineDefinitionLoadResult Empty { get; } = new([], []);
}
