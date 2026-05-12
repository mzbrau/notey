using Notey.Pipelines.Steps;

namespace Notey.Pipelines.Registry;

public interface IPipelineStepRegistry
{
    IReadOnlyCollection<IPipelineStep> Steps { get; }

    bool TryGet(string stepId, out IPipelineStep step);
}
