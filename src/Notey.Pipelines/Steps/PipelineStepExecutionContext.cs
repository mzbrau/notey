using Notey.Pipelines.Context;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Progress;

namespace Notey.Pipelines.Steps;

public sealed record PipelineStepExecutionContext(
    PipelineDefinition Pipeline,
    PipelineStepDefinition Step,
    PipelineContext Context,
    IProgress<PipelineProgressUpdate>? Progress);
