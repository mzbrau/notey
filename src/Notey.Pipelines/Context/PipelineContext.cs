namespace Notey.Pipelines.Context;

public sealed class PipelineContext
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PipelineWarning> _warnings = [];

    public PipelineContext(string pipelineId, DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);

        PipelineId = pipelineId;
        StartedAt = startedAt;
        ExecutionId = Guid.NewGuid();
    }

    public string PipelineId { get; }

    public Guid ExecutionId { get; }

    public DateTimeOffset StartedAt { get; }

    public IReadOnlyDictionary<string, object?> Values => _values;

    public IReadOnlyList<PipelineWarning> Warnings => _warnings;

    public void SetValue(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _values[key] = value;
    }

    public bool TryGetValue<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_values.TryGetValue(key, out var rawValue) && rawValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    public void AddWarning(string message, string? sourceStepId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        _warnings.Add(new PipelineWarning(message, sourceStepId));
    }
}

public sealed record PipelineWarning(string Message, string? SourceStepId);
