using Notey.Pipelines.Steps;

namespace Notey.Pipelines.Registry;

public sealed class PipelineStepRegistry : IPipelineStepRegistry
{
    private readonly Dictionary<string, IPipelineStep> _steps = new(StringComparer.OrdinalIgnoreCase);

    public PipelineStepRegistry(IEnumerable<IPipelineStep> steps)
    {
        foreach (var step in steps)
        {
            Register(step);
        }
    }

    public IReadOnlyCollection<IPipelineStep> Steps => _steps.Values;

    public bool TryGet(string stepId, out IPipelineStep step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        return _steps.TryGetValue(stepId, out step!);
    }

    private void Register(IPipelineStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(step.Id);

        if (!_steps.TryAdd(step.Id, step))
        {
            throw new InvalidOperationException($"A pipeline step with id '{step.Id}' is already registered.");
        }
    }
}
