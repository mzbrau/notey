using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Execution;

namespace Notey.Pipelines.Steps;

public abstract class PipelineStep<TInput, TOutput> : IPipelineStep
    where TInput : PipelineData
    where TOutput : PipelineData
{
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public PipelineDataType OutputType { get; }

    public IReadOnlySet<PipelineDataType> AcceptedInputTypes { get; }

    protected PipelineStep(PipelineDataType inputType)
        : this(new HashSet<PipelineDataType> { inputType })
    {
    }

    protected PipelineStep(IReadOnlySet<PipelineDataType> acceptedInputTypes)
    {
        ArgumentNullException.ThrowIfNull(acceptedInputTypes);

        if (acceptedInputTypes.Count == 0 || acceptedInputTypes.Contains(PipelineDataType.Unknown))
        {
            throw new InvalidOperationException($"Step '{GetType().Name}' must declare at least one known input contract.");
        }

        if (!PipelineDataContracts.TryGetDataType(typeof(TOutput), out var outputType))
        {
            throw new InvalidOperationException($"Step '{GetType().Name}' declares unsupported output CLR type {typeof(TOutput).Name}.");
        }

        if (typeof(TInput) != typeof(PipelineData))
        {
            foreach (var inputType in acceptedInputTypes)
            {
                if (!PipelineDataContracts.TryGetClrType(inputType, out var clrType) || clrType != typeof(TInput))
                {
                    throw new InvalidOperationException(
                        $"Step '{GetType().Name}' declares input contract {inputType}, but its CLR input type is {typeof(TInput).Name}.");
                }
            }
        }

        OutputType = outputType;
        AcceptedInputTypes = new HashSet<PipelineDataType>(acceptedInputTypes);
    }

    public virtual IReadOnlyList<string> ValidateConfiguration(PipelineStepDefinition definition) => [];

    public async ValueTask<PipelineStepResult> ExecuteAsync(
        PipelineData input,
        PipelineStepExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!AcceptedInputTypes.Contains(input.Type))
        {
            throw new PipelineExecutionException(
                $"Step '{Id}' cannot consume runtime input type {input.Type}.",
                context.Context);
        }

        if (input is not TInput typedInput)
        {
            throw new PipelineExecutionException(
                $"Step '{Id}' expected input CLR type {typeof(TInput).Name}, but received {input.GetType().Name}.",
                context.Context);
        }

        var output = await ExecuteTypedAsync(typedInput, context, cancellationToken);
        return new PipelineStepResult(output.Output, output.Message);
    }

    protected abstract ValueTask<PipelineStepResult<TOutput>> ExecuteTypedAsync(
        TInput input,
        PipelineStepExecutionContext context,
        CancellationToken cancellationToken);
}
