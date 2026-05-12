using Notey.Pipelines.Context;

namespace Notey.Pipelines.Execution;

public sealed class PipelineExecutionException : InvalidOperationException
{
    public PipelineExecutionException(string message, PipelineContext context, Exception? innerException = null)
        : base(message, innerException)
    {
        Context = context;
    }

    public PipelineContext Context { get; }
}
