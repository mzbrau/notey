namespace Notey.Pipelines.Definitions;

public interface IPipelineDefinitionSource
{
    ValueTask<PipelineDefinitionLoadResult> LoadAsync(CancellationToken cancellationToken = default);
}
