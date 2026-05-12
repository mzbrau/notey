namespace Notey.Pipelines.Progress;

public sealed record PipelineProgressUpdate(
    string PipelineId,
    PipelineProgressStatus Status,
    int CompletedSteps,
    int TotalSteps,
    string? StepId = null,
    string? Message = null);
