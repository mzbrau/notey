using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;

namespace Notey.Pipelines.Steps;

public interface IPipelineStep
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlySet<PipelineDataType> AcceptedInputTypes { get; }

    PipelineDataType OutputType { get; }

    IReadOnlyList<string> ValidateConfiguration(PipelineStepDefinition definition);

    ValueTask<PipelineStepResult> ExecuteAsync(
        PipelineData input,
        PipelineStepExecutionContext context,
        CancellationToken cancellationToken = default);
}
