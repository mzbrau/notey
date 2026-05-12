using Notey.Pipelines.Data;

namespace Notey.Pipelines.Validation;

public sealed record PipelineValidationResult(
    string PipelineId,
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    PipelineDataType? ResolvedOutputType);
