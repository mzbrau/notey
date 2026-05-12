using Notey.Pipelines.Data;

namespace Notey.Pipelines.Steps;

public sealed record PipelineStepResult(PipelineData Output, string? Message = null);

public sealed record PipelineStepResult<TOutput>(TOutput Output, string? Message = null)
    where TOutput : PipelineData;
