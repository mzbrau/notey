using Notey.Pipelines.Definitions;
using Notey.Pipelines.Validation;

namespace Notey.Pipelines.Catalog;

public sealed record PipelineCatalogSnapshot(
    IReadOnlyList<PipelineCatalogEntry> Entries,
    IReadOnlyDictionary<string, PipelineValidationResult> ValidationResults,
    IReadOnlyList<string> LoadWarnings)
{
    public IReadOnlyList<PipelineDefinition> Definitions => Entries
        .Select(entry => entry.Definition)
        .ToArray();
}

public sealed record PipelineCatalogEntry(
    PipelineDefinition Definition,
    PipelineValidationResult ValidationResult);
