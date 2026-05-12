using Notey.Pipelines.Data;
using System.Text.Json.Nodes;

namespace Notey.Pipelines.Definitions;

public sealed class PipelineDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public List<PipelineDataType> AcceptedInputTypes { get; init; } = [];

    public List<PipelineStepDefinition> Steps { get; init; } = [];

    public PipelineDataType FinalOutputType { get; init; } = PipelineDataType.Unknown;
}

public sealed class PipelineStepDefinition
{
    public string Id { get; init; } = string.Empty;

    public string StepId { get; init; } = string.Empty;

    public JsonObject? Configuration { get; init; }
}
