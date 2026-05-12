namespace Notey.Pipelines.Validation;

public sealed class PipelineValidationException : InvalidOperationException
{
    public PipelineValidationException(string message, IReadOnlyList<string> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
