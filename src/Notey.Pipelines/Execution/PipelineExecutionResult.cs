using Notey.Pipelines.Context;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;

namespace Notey.Pipelines.Execution;

public sealed record PipelineExecutionResult(
    PipelineDefinition Pipeline,
    PipelineData Output,
    PipelineContext Context);
