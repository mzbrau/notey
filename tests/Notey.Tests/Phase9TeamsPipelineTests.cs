using System.Text.Json.Nodes;
using Notey.AI.Providers;
using Notey.Core.Configuration;
using Notey.Ocr;
using Notey.Pipelines.Catalog;
using Notey.Pipelines.Context;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Execution;
using Notey.Pipelines.Registry;
using Notey.Pipelines.Steps;
using Notey.Pipelines.Validation;
using Notey.PipelineSteps;

namespace Notey.Tests;

public sealed class Phase9TeamsPipelineTests
{
    [Fact]
    public void Ai_parser_accepts_teams_aliases_for_participants_and_sections()
    {
        const string response = """
            {
              "summary": "Planning sync.",
              "meeting_title": "Apollo planning",
              "people": [],
              "participants": [
                { "name": "Jane Doe", "confidence": 0.93, "source": "participant rail" }
              ],
              "agenda": "Roadmap review",
              "action_items": [ "Send recap", "Confirm launch date" ]
            }
            """;

        var note = AiStructuredResponseParser.Parse(response);

        Assert.Equal("Apollo planning", note.MeetingTitle);
        Assert.NotNull(note.People);
        var participant = Assert.Single(note.People);
        Assert.Equal("Jane Doe", participant.Name);
        Assert.Equal(0.93, participant.Confidence);
        Assert.NotNull(note.Sections);
        Assert.Equal("Roadmap review", note.Sections["Agenda"]);
        Assert.Equal("- Send recap\n- Confirm launch date", note.Sections["Action items"]);
    }

    [Fact]
    public async Task Teams_normalizer_promotes_only_confident_participants()
    {
        var step = new TeamsMeetingNormalizerStep();
        var pipeline = new PipelineDefinition
        {
            Id = "teams-normalizer",
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "normalize",
                    StepId = TeamsMeetingNormalizerStep.StepTypeId,
                    Configuration = new JsonObject
                    {
                        ["participantConfidenceThreshold"] = 0.75,
                        ["tags"] = "teams,meeting",
                    },
                },
            ],
        };
        var context = new PipelineContext(pipeline.Id, DateTimeOffset.UtcNow);

        var result = await step.ExecuteAsync(
            new StructuredNoteData(
                MeetingTitle: "  Apollo   Sync  ",
                People:
                [
                    new EntitySuggestion("Jane Doe", "person", 0.94, "participants"),
                    new EntitySuggestion("Jane Doe (Guest)", "person", 0.62, "caption"),
                    new EntitySuggestion("Microsoft Teams", "person", 0.99, "chrome"),
                    new EntitySuggestion("Alex Kim", "person", 91, "percent"),
                    new EntitySuggestion("Sam Lee (External)", "person", null, "partial"),
                ],
                Tags: ["planning"],
                Sections: new Dictionary<string, string>
                {
                    ["Agenda"] = "  Roadmap   review  ",
                    ["Action items"] = "- Send   recap\n- Confirm   launch date",
                }),
            new PipelineStepExecutionContext(pipeline, pipeline.Steps[0], context, null));

        var output = Assert.IsType<StructuredNoteData>(result.Output);
        Assert.Equal("Apollo Sync", output.MeetingTitle);
        Assert.NotNull(output.People);
        Assert.Equal("Jane Doe", Assert.Single(output.People).Name);
        Assert.NotNull(output.Topics);
        Assert.Equal("Teams meeting", Assert.Single(output.Topics).Name);
        Assert.NotNull(output.Tags);
        Assert.Equal(["planning", "teams", "meeting"], output.Tags);
        Assert.NotNull(output.Sections);
        Assert.Equal("Roadmap review", output.Sections["Agenda"]);
        Assert.Equal("- Send recap\n- Confirm launch date", output.Sections["Action items"]);
        Assert.Contains("Alex Kim", output.Sections["Suggested participants to review"], StringComparison.Ordinal);
        Assert.Contains("Sam Lee", output.Sections["Suggested participants to review"], StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft Teams", output.Sections["Suggested participants to review"], StringComparison.Ordinal);
        Assert.True(context.TryGetValue<string[]>(PipelineStepContextKeys.TeamsSuggestedParticipants, out var suggestions));
        Assert.NotNull(suggestions);
        Assert.Contains("Sam Lee", suggestions);
        Assert.Contains(context.Warnings, warning => warning.SourceStepId == "normalize");
    }

    [Fact]
    public void Teams_normalizer_rejects_invalid_confidence_threshold()
    {
        var step = new TeamsMeetingNormalizerStep();
        var outOfRangeErrors = step.ValidateConfiguration(new PipelineStepDefinition
        {
            Id = "normalize",
            StepId = TeamsMeetingNormalizerStep.StepTypeId,
            Configuration = new JsonObject { ["participantConfidenceThreshold"] = 1.5 },
        });
        var malformedErrors = step.ValidateConfiguration(new PipelineStepDefinition
        {
            Id = "normalize",
            StepId = TeamsMeetingNormalizerStep.StepTypeId,
            Configuration = new JsonObject { ["participantConfidenceThreshold"] = "strict" },
        });

        Assert.Contains(outOfRangeErrors, error => error.Contains("participantConfidenceThreshold", StringComparison.Ordinal));
        Assert.Contains(malformedErrors, error => error.Contains("participantConfidenceThreshold", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Default_pipeline_config_includes_valid_teams_pipeline()
    {
        var registry = CreatePhase9Registry(
            new RecordingOcrEngine(new OcrResult("ocr", "eng", 0.9, [])),
            new RecordingAiProvider("default", """{ "summary": "ok" }""", "model"));
        var catalog = new PipelineCatalog(
            new FilePipelineDefinitionSource(Path.Combine(FindRepoRoot(), "src", "Notey.App", "pipelines.json")),
            new PipelineValidator(registry));

        var snapshot = await catalog.LoadAsync();
        var compatible = await catalog.GetEnabledCompatibleAsync(PipelineDataType.ImageData);

        Assert.Contains(snapshot.Entries, entry => entry.Definition.Id == "teams-screenshot-ocr-ai-structured" && entry.ValidationResult.IsValid);
        Assert.Contains(compatible, pipeline => pipeline.Id == "screenshot-ocr-ai-structured");
        Assert.Contains(compatible, pipeline => pipeline.Id == "teams-screenshot-ocr-ai-structured");
    }

    [Fact]
    public async Task Teams_pipeline_round_trip_keeps_uncertain_participants_out_of_metadata_people()
    {
        var ocr = new RecordingOcrEngine(new OcrResult("Teams OCR text", "eng", 0.88, []));
        var provider = new RecordingAiProvider(
            "default",
            """
            {
              "summary": "Discussed launch readiness.",
              "meetingTitle": "Launch readiness",
              "participants": [
                { "name": "Jane Doe", "confidence": 0.91, "source": "participant rail" },
                { "name": "Sam", "confidence": 0.41, "source": "partial caption" }
              ],
              "actionItems": [ "Send launch notes" ]
            }
            """,
            "gpt-test");
        var registry = CreatePhase9Registry(ocr, provider);
        var pipeline = CreateTeamsPipelineDefinition();
        var result = await new PipelineExecutor(registry, new PipelineValidator(registry), TimeProvider.System)
            .ExecuteAsync(pipeline, new ImageData("teams.png", DateTimeOffset.UtcNow, 1200, 800));

        var output = Assert.IsType<StructuredNoteData>(result.Output);
        Assert.NotNull(output.People);
        Assert.Equal("Jane Doe", Assert.Single(output.People).Name);
        Assert.NotNull(output.Sections);
        Assert.Contains("Sam", output.Sections["Suggested participants to review"], StringComparison.Ordinal);
        Assert.Contains("Send launch notes", output.Sections["Action items"], StringComparison.Ordinal);
        Assert.Contains("Teams OCR text", provider.LastRequest?.Prompt, StringComparison.Ordinal);
        Assert.Contains(result.Context.Warnings, warning => warning.SourceStepId == "normalize");
    }

    private static PipelineStepRegistry CreatePhase9Registry(ITesseractOcrEngine ocrEngine, IAiProvider aiProvider)
    {
        IPipelineStep[] steps =
        [
            new TesseractOcrStep(ocrEngine, new NoteyOptions()),
            new AiStructuredExtractionStep(new AiProviderRegistry([aiProvider], "default")),
            new TeamsMeetingNormalizerStep(),
        ];
        return new PipelineStepRegistry(steps);
    }

    private static PipelineDefinition CreateTeamsPipelineDefinition()
    {
        return new PipelineDefinition
        {
            Id = "teams-screenshot-ocr-ai-structured",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.StructuredNoteData,
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "ocr",
                    StepId = TesseractOcrStep.StepTypeId,
                    Configuration = new JsonObject { ["language"] = "eng" },
                },
                new PipelineStepDefinition
                {
                    Id = "extract",
                    StepId = AiStructuredExtractionStep.StepTypeId,
                    Configuration = new JsonObject { ["providerId"] = "default" },
                },
                new PipelineStepDefinition
                {
                    Id = "normalize",
                    StepId = TeamsMeetingNormalizerStep.StepTypeId,
                    Configuration = new JsonObject { ["participantConfidenceThreshold"] = 0.75 },
                },
            ],
        };
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Notey.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class RecordingOcrEngine(OcrResult result) : ITesseractOcrEngine
    {
        public ValueTask<OcrResult> RecognizeAsync(
            TesseractOcrRequest request,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingAiProvider(string id, string responseText, string responseModel) : IAiProvider
    {
        public string Id => id;

        public AiTextRequest? LastRequest { get; private set; }

        public ValueTask<AiTextResponse> CompleteTextAsync(
            AiTextRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return ValueTask.FromResult(new AiTextResponse(responseText, Id, responseModel));
        }
    }
}
