using System.Text.Json.Nodes;
using Notey.AI.Providers;
using Notey.Core.Configuration;
using Notey.Ocr;
using Notey.PipelineSteps;
using Notey.Pipelines.Catalog;
using Notey.Pipelines.Context;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Execution;
using Notey.Pipelines.Registry;
using Notey.Pipelines.Steps;
using Notey.Pipelines.Validation;

namespace Notey.Tests;

public sealed class Phase10OrganizationAssistanceTests
{
    [Fact]
    public void Organization_input_excludes_frontmatter_context_and_prior_ai_cleanup()
    {
        var input = NoteOrganizationMarkdown.BuildOrganizationInput(
            """
            ---
            people:
              - "[[People/Jane Doe|Jane Doe]]"
            ---
            <!-- notey-context:start -->
            ## Context
            - People: [[People/Jane Doe|Jane Doe]]
            <!-- notey-context:end -->

            # Untitled note

            Original user note line.

            <!-- notey-ai-cleanup:start -->
            ## AI cleaned summary

            Old generated summary.
            <!-- notey-ai-cleanup:end -->
            """,
            ["Jane Doe"],
            ["Roadmap"],
            ["Apollo"],
            ["meeting"]);

        Assert.Contains("Original user note line.", input, StringComparison.Ordinal);
        Assert.Contains("- People: Jane Doe", input, StringComparison.Ordinal);
        Assert.Contains("- Tags: #meeting", input, StringComparison.Ordinal);
        Assert.DoesNotContain("Old generated summary", input, StringComparison.Ordinal);
        Assert.DoesNotContain("notey-context", input, StringComparison.Ordinal);
        Assert.DoesNotContain("people:", input, StringComparison.Ordinal);
    }

    [Fact]
    public void Organization_cleanup_block_replaces_previous_generated_cleanup()
    {
        var cleanup = NoteOrganizationMarkdown.RenderCleanupBlock(
            new StructuredNoteData(
                Summary: "Clean summary.",
                Sections: new Dictionary<string, string> { ["Actions"] = "- Follow up" },
                Tags: ["meeting"]),
            "AI cleaned summary");

        var result = NoteOrganizationMarkdown.ReplaceCleanupBlock(
            """
            # Notes

            Raw note text.

            <!-- notey-ai-cleanup:start -->
            ## AI cleaned summary

            Old generated summary.
            <!-- notey-ai-cleanup:end -->
            """,
            cleanup);

        Assert.Contains("Raw note text.", result, StringComparison.Ordinal);
        Assert.Contains("Clean summary.", result, StringComparison.Ordinal);
        Assert.Contains("### Actions\n- Follow up", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Suggested tags", result, StringComparison.Ordinal);
        Assert.DoesNotContain("#meeting", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Old generated summary", result, StringComparison.Ordinal);
        Assert.Single(FindAll(result, NoteOrganizationMarkdown.CleanupStartMarker));
    }

    [Fact]
    public void Organization_cleanup_block_throws_for_null_heading()
    {
        var data = new StructuredNoteData(
            Summary: "Clean summary.",
            Sections: new Dictionary<string, string> { ["Actions"] = "- Follow up" },
            Tags: ["meeting"]);

        Assert.Throws<ArgumentNullException>(() => NoteOrganizationMarkdown.RenderCleanupBlock(data, null!));
    }

    [Fact]
    public async Task Organization_pipeline_returns_reviewable_structured_suggestions()
    {
        var provider = new RecordingAiProvider(
            "default",
            """
            {
              "summary": "Cleaned note summary.",
              "people": ["Jane Doe"],
              "topics": ["Roadmap"],
              "projects": ["Apollo"],
              "tags": ["meeting"],
              "sections": { "Actions": "- Send recap" }
            }
            """,
            "gpt-test");
        var registry = new PipelineStepRegistry([
            new AiStructuredExtractionStep(new AiProviderRegistry([provider], "default")),
        ]);
        var pipeline = new PipelineDefinition
        {
            Id = "note-organization-ai-structured",
            AcceptedInputTypes = [PipelineDataType.TextData],
            FinalOutputType = PipelineDataType.StructuredNoteData,
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "organize",
                    StepId = AiStructuredExtractionStep.StepTypeId,
                    Configuration = new JsonObject
                    {
                        ["providerId"] = "default",
                        ["systemPrompt"] = "System",
                        ["taskPrompt"] = "Organize note",
                    },
                },
            ],
        };

        var result = await new PipelineExecutor(registry, new PipelineValidator(registry), TimeProvider.System)
            .ExecuteAsync(pipeline, new TextData("Original note text."));

        var output = Assert.IsType<StructuredNoteData>(result.Output);
        Assert.Equal("Cleaned note summary.", output.Summary);
        Assert.NotNull(output.People);
        Assert.Equal("Jane Doe", Assert.Single(output.People).Name);
        Assert.NotNull(output.Tags);
        Assert.Equal(["meeting"], output.Tags);
        Assert.Contains("Original note text.", provider.LastRequest?.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Default_pipeline_config_includes_valid_text_organization_pipeline()
    {
        var registry = CreateRegistry(
            new RecordingOcrEngine(new OcrResult("ocr", "eng", 0.9, [])),
            new RecordingAiProvider("default", """{ "summary": "ok" }""", "model"));
        var catalog = new PipelineCatalog(
            new FilePipelineDefinitionSource(Path.Combine(FindRepoRoot(), "src", "Notey.App", "pipelines.json")),
            new PipelineValidator(registry));

        var snapshot = await catalog.LoadAsync();
        var compatible = await catalog.GetEnabledCompatibleAsync(PipelineDataType.TextData);

        Assert.Contains(snapshot.Entries, entry => entry.Definition.Id == "note-organization-ai-structured" && entry.ValidationResult.IsValid);
        Assert.Contains(compatible, pipeline => pipeline.Id == "note-organization-ai-structured");
    }

    private static PipelineStepRegistry CreateRegistry(ITesseractOcrEngine ocrEngine, IAiProvider aiProvider)
    {
        IPipelineStep[] steps =
        [
            new TesseractOcrStep(ocrEngine, new NoteyOptions()),
            new AiStructuredExtractionStep(new AiProviderRegistry([aiProvider], "default")),
            new TeamsMeetingNormalizerStep(),
            new MarkdownAssemblyStep(),
        ];
        return new PipelineStepRegistry(steps);
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

    private static IEnumerable<int> FindAll(string text, string value)
    {
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            yield return index;
            index += value.Length;
        }
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
