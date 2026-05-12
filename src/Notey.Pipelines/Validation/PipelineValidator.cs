using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Registry;

namespace Notey.Pipelines.Validation;

public sealed class PipelineValidator(IPipelineStepRegistry registry)
{
    public static IReadOnlySet<PipelineDataType> AllowedFinalOutputTypes { get; } = new HashSet<PipelineDataType>
    {
        PipelineDataType.StructuredNoteData,
        PipelineDataType.MarkdownContent,
    };

    public PipelineValidationResult Validate(PipelineDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            errors.Add("Pipeline id is required.");
        }

        if (!definition.Enabled)
        {
            warnings.Add($"Pipeline '{definition.Id}' is disabled.");
        }

        var configuredInputTypes = definition.AcceptedInputTypes ?? [];
        var configuredSteps = definition.Steps ?? [];

        var acceptedInputTypes = configuredInputTypes
            .Where(type => type != PipelineDataType.Unknown)
            .Distinct()
            .ToArray();

        if (acceptedInputTypes.Length == 0)
        {
            errors.Add($"Pipeline '{definition.Id}' must declare at least one accepted input type.");
        }

        if (configuredInputTypes.Any(type => type == PipelineDataType.Unknown))
        {
            errors.Add($"Pipeline '{definition.Id}' declares an unknown accepted input type.");
        }

        if (configuredSteps.Count == 0)
        {
            errors.Add($"Pipeline '{definition.Id}' must declare at least one step.");
        }

        if (!AllowedFinalOutputTypes.Contains(definition.FinalOutputType))
        {
            errors.Add($"Pipeline '{definition.Id}' final output type must be StructuredNoteData or MarkdownContent.");
        }

        PipelineDataType? currentType = acceptedInputTypes.Length == 1 ? acceptedInputTypes[0] : null;
        IReadOnlyCollection<PipelineDataType> currentTypes = acceptedInputTypes;

        foreach (var stepDefinition in configuredSteps)
        {
            if (stepDefinition is null)
            {
                errors.Add($"Pipeline '{definition.Id}' contains a null step definition.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(stepDefinition.Id))
            {
                errors.Add($"Pipeline '{definition.Id}' has a step without an id.");
            }

            if (string.IsNullOrWhiteSpace(stepDefinition.StepId))
            {
                errors.Add($"Pipeline '{definition.Id}' step '{stepDefinition.Id}' must reference a registered step id.");
                continue;
            }

            if (!registry.TryGet(stepDefinition.StepId, out var step))
            {
                errors.Add($"Pipeline '{definition.Id}' step '{stepDefinition.Id}' references unknown step id '{stepDefinition.StepId}'.");
                continue;
            }

            foreach (var configurationError in step.ValidateConfiguration(stepDefinition))
            {
                errors.Add($"Pipeline '{definition.Id}' step '{stepDefinition.Id}' configuration is invalid: {configurationError}");
            }

            foreach (var inputType in currentTypes)
            {
                if (!step.AcceptedInputTypes.Contains(inputType))
                {
                    errors.Add(
                        $"Pipeline '{definition.Id}' step '{stepDefinition.Id}' cannot consume {inputType}; it accepts {string.Join(", ", step.AcceptedInputTypes)}.");
                }
            }

            currentType = step.OutputType;
            currentTypes = [step.OutputType];
        }

        if (currentType is not null && currentType != definition.FinalOutputType)
        {
            errors.Add(
                $"Pipeline '{definition.Id}' final output type is {definition.FinalOutputType}, but the last step produces {currentType}.");
        }

        return new PipelineValidationResult(
            definition.Id,
            errors.Count == 0,
            errors,
            warnings,
            currentType);
    }
}
