using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Validation;

namespace Notey.Pipelines.Catalog;

public sealed class PipelineCatalog(
    IPipelineDefinitionSource source,
    PipelineValidator validator)
{
    public async ValueTask<PipelineCatalogSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var loadResult = await source.LoadAsync(cancellationToken);
        var entries = new List<PipelineCatalogEntry>();
        var validationResults = new Dictionary<string, PipelineValidationResult>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>(loadResult.Warnings);
        var duplicateIds = loadResult.Definitions
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Id))
            .GroupBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var duplicateId in duplicateIds)
        {
            warnings.Add($"Pipeline id '{duplicateId}' is duplicated.");
        }

        for (var index = 0; index < loadResult.Definitions.Count; index++)
        {
            var definition = loadResult.Definitions[index];
            var validation = validator.Validate(definition);
            var validationKey = GetValidationKey(definition, index);

            if (!string.IsNullOrWhiteSpace(definition.Id) && duplicateIds.Contains(definition.Id))
            {
                var duplicateError = $"Pipeline id '{definition.Id}' is duplicated.";
                validation = validation with
                {
                    IsValid = false,
                    Errors = validation.Errors.Concat([duplicateError]).ToArray(),
                };
                validationKey = $"{definition.Id}#{index + 1}";
            }

            validationResults[validationKey] = validation;
            entries.Add(new PipelineCatalogEntry(definition, validation));
        }

        return new PipelineCatalogSnapshot(
            entries,
            validationResults,
            warnings);
    }

    public async ValueTask<IReadOnlyList<PipelineDefinition>> GetEnabledCompatibleAsync(
        PipelineDataType inputType,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadAsync(cancellationToken);

        return snapshot.Entries
            .Where(entry =>
                entry.Definition.Enabled &&
                entry.Definition.AcceptedInputTypes is not null &&
                entry.Definition.AcceptedInputTypes.Contains(inputType) &&
                entry.ValidationResult.IsValid)
            .Select(entry => entry.Definition)
            .ToArray();
    }

    private static string GetValidationKey(PipelineDefinition definition, int index)
    {
        return string.IsNullOrWhiteSpace(definition.Id)
            ? $"<missing:{index + 1}>"
            : definition.Id;
    }
}
