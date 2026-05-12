using Notey.AI.Providers;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Execution;
using Notey.Pipelines.Steps;

namespace Notey.PipelineSteps;

public sealed class AiStructuredExtractionStep(IAiProviderRegistry providerRegistry)
    : PipelineStep<PipelineData, StructuredNoteData>(
        new HashSet<PipelineDataType> { PipelineDataType.TextData, PipelineDataType.OcrTextData })
{
    public const string StepTypeId = "ai-structured-extraction";

    private const string DefaultSystemPrompt =
        "You transform text into structured note context. Return only a JSON object.";

    private const string DefaultTaskPrompt =
        "Extract summary, meetingTitle, people, topics, projects, tags, and sections from the input text.";

    public override string Id => StepTypeId;

    public override string DisplayName => "AI structured extraction";

    public override IReadOnlyList<string> ValidateConfiguration(PipelineStepDefinition definition)
    {
        var errors = new List<string>();
        var temperature = StepConfigurationReader.GetDouble(definition.Configuration, "temperature");
        var maxTokens = StepConfigurationReader.GetInt32(definition.Configuration, "maxTokens");

        if (temperature is < 0 or > 2)
        {
            errors.Add("temperature must be between 0 and 2.");
        }

        if (maxTokens is <= 0)
        {
            errors.Add("maxTokens must be greater than zero.");
        }

        return errors;
    }

    protected override async ValueTask<PipelineStepResult<StructuredNoteData>> ExecuteTypedAsync(
        PipelineData input,
        PipelineStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        var text = input switch
        {
            TextData textData => textData.Text,
            OcrTextData ocrTextData => ocrTextData.Text,
            _ => throw new PipelineExecutionException(
                $"Step '{Id}' cannot extract structured data from {input.Type}.",
                context.Context),
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            context.Context.AddWarning("AI extraction received empty text.", context.Step.Id);
            return new PipelineStepResult<StructuredNoteData>(new StructuredNoteData(), "No text available for AI extraction.");
        }

        var providerId = StepConfigurationReader.GetString(context.Step.Configuration, "providerId");
        if (!providerRegistry.TryGet(providerId, out var provider))
        {
            throw new PipelineExecutionException(
                $"AI provider '{providerId ?? "<default>"}' is not configured.",
                context.Context);
        }

        var systemPrompt = StepConfigurationReader.GetString(context.Step.Configuration, "systemPrompt") ?? DefaultSystemPrompt;
        var taskPrompt = StepConfigurationReader.GetString(context.Step.Configuration, "taskPrompt") ?? DefaultTaskPrompt;
        var prompt = BuildPrompt(taskPrompt, text);
        var response = await provider.CompleteTextAsync(
            new AiTextRequest(
                prompt,
                systemPrompt,
                StepConfigurationReader.GetString(context.Step.Configuration, "modelName"),
                JsonOutput: true,
                Temperature: StepConfigurationReader.GetDouble(context.Step.Configuration, "temperature"),
                MaxTokens: StepConfigurationReader.GetInt32(context.Step.Configuration, "maxTokens")),
            cancellationToken);

        context.Context.SetValue(PipelineStepContextKeys.AiProviderId, response.ProviderId);
        context.Context.SetValue(PipelineStepContextKeys.AiModelName, response.ModelName);
        if (StepConfigurationReader.GetBoolean(context.Step.Configuration, "preserveRawOutput"))
        {
            context.Context.SetValue(PipelineStepContextKeys.AiRawOutput, response.Text);
        }

        try
        {
            return new PipelineStepResult<StructuredNoteData>(
                AiStructuredResponseParser.Parse(response.Text),
                $"Structured note data extracted with {response.ProviderId}/{response.ModelName}.");
        }
        catch (FormatException ex)
        {
            throw new PipelineExecutionException("AI structured extraction returned invalid JSON.", context.Context, ex);
        }
    }

    private static string BuildPrompt(string taskPrompt, string inputText)
    {
        return $$"""
            {{taskPrompt}}

            Return JSON using this shape:
            {
              "summary": "short factual summary",
              "meetingTitle": "optional meeting title",
              "people": [{ "name": "person", "confidence": 0.0, "source": "optional" }],
              "topics": [{ "name": "topic", "confidence": 0.0, "source": "optional" }],
              "projects": [{ "name": "project", "confidence": 0.0, "source": "optional" }],
              "tags": ["tag"],
              "sections": { "Section heading": "Markdown-ready text" }
            }

            Input text:
            {{inputText}}
            """;
    }
}
