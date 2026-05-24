using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Notey.AI.Providers;
using Notey.Core.Configuration;
using Notey.Ocr;
using Notey.Pipelines.Context;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Execution;
using Notey.Pipelines.Steps;
using Notey.PipelineSteps;

namespace Notey.Tests;

public sealed class Phase8PipelineStepTests
{
    [Fact]
    public void Ai_parser_reads_fenced_json_and_entity_suggestions()
    {
        const string response = """
            ```json
            {
              "summary": "Discussed launch risks.",
              "meeting_title": "Launch review",
              "people": [{ "name": "Jane Doe", "confidence": 0.91, "source": "ocr" }],
              "topics": ["Release Planning"],
              "projects": [{ "title": "Apollo" }],
              "tags": ["launch", "risk"],
              "sections": { "Actions": "- Follow up with QA" }
            }
            ```
            """;

        var note = AiStructuredResponseParser.Parse(response);

        Assert.Equal("Discussed launch risks.", note.Summary);
        Assert.Equal("Launch review", note.MeetingTitle);
        Assert.NotNull(note.People);
        var person = Assert.Single(note.People);
        Assert.Equal("Jane Doe", person.Name);
        Assert.Equal(0.91, person.Confidence);
        Assert.Equal("ocr", person.Source);
        Assert.NotNull(note.Topics);
        Assert.Equal("Release Planning", Assert.Single(note.Topics).Name);
        Assert.NotNull(note.Projects);
        Assert.Equal("Apollo", Assert.Single(note.Projects).Name);
        Assert.NotNull(note.Tags);
        Assert.Equal(["launch", "risk"], note.Tags);
        Assert.NotNull(note.Sections);
        Assert.Equal("- Follow up with QA", note.Sections["Actions"]);
    }

    [Fact]
    public void Ai_parser_rejects_malformed_responses()
    {
        var exception = Assert.Throws<FormatException>(() => AiStructuredResponseParser.Parse("not json"));

        Assert.Contains("JSON object", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ai_parser_ignores_non_string_values_for_string_fields()
    {
        const string response = """
            { "summary": 42, "tags": [1, " useful "] }
            """;

        var note = AiStructuredResponseParser.Parse(response);

        Assert.Null(note.Summary);
        Assert.NotNull(note.Tags);
        Assert.Equal(["useful"], note.Tags);
    }

    [Fact]
    public async Task Open_ai_provider_factory_uses_environment_api_key_when_config_is_blank()
    {
        const string variableName = "NOTEY_TEST_AI_KEY";
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        Environment.SetEnvironmentVariable(variableName, "env-api-key");
        try
        {
            var handler = new RecordingHandler("""
                { "choices": [{ "message": { "content": "{\"summary\":\"ok\"}" } }] }
                """);
            var options = new AiOptions
            {
                BaseUrl = "https://example.test/v1?api-version=2024-06-01",
                ModelName = "test-model",
                ApiKeyEnvironmentVariable = variableName,
            };

            var provider = Assert.Single(OpenAiCompatibleAiProviderFactory.CreateProviders(
                options,
                () => new HttpClient(handler),
                NullLoggerFactory.Instance));
            var response = await provider.CompleteTextAsync(new AiTextRequest("Summarize", JsonOutput: true));

            Assert.Equal("{\"summary\":\"ok\"}", response.Text);
            Assert.Equal("Bearer", handler.Authorization?.Scheme);
            Assert.Equal("env-api-key", handler.Authorization?.Parameter);
            Assert.Equal(new Uri("https://example.test/v1/chat/completions?api-version=2024-06-01"), handler.RequestUri);
            Assert.Contains("\"response_format\"", handler.RequestBody, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }

    [Fact]
    public async Task Open_ai_provider_factory_uses_global_api_key_for_provider_when_provider_key_is_blank()
    {
        var handler = new RecordingHandler("""
            { "choices": [{ "message": { "content": "{\"summary\":\"ok\"}" } }] }
            """);
        var options = new AiOptions
        {
            BaseUrl = "https://example.test/v1",
            ModelName = "test-model",
            ApiKey = "global-key",
            Providers =
            [
                new AiProviderOptions
                {
                    Id = "custom",
                    Type = "OpenAiCompatible",
                    BaseUrl = "https://example.test/v2",
                    ModelName = "provider-model",
                    ApiKey = " ",
                },
            ],
        };

        var providers = OpenAiCompatibleAiProviderFactory.CreateProviders(options, () => new HttpClient(handler), NullLoggerFactory.Instance);
        var provider = Assert.Single(providers, static candidate => candidate.Id == "custom");
        await provider.CompleteTextAsync(new AiTextRequest("Summarize", JsonOutput: true));

        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("global-key", handler.Authorization?.Parameter);
        Assert.Equal(new Uri("https://example.test/v2/chat/completions"), handler.RequestUri);
    }

    [Fact]
    public async Task Open_ai_provider_sends_temperature_and_max_tokens_for_standard_model()
    {
        var handler = new RecordingHandler("""
            { "choices": [{ "message": { "content": "result" } }] }
            """);
        var options = new AiOptions
        {
            BaseUrl = "https://example.test/v1",
            ModelName = "gpt-4o",
            ApiKey = "key",
            ReasoningModel = false,
        };

        var provider = Assert.Single(OpenAiCompatibleAiProviderFactory.CreateProviders(options, () => new HttpClient(handler), NullLoggerFactory.Instance));
        await provider.CompleteTextAsync(new AiTextRequest("Summarize", Temperature: 0.1, MaxTokens: 1024));

        Assert.Contains("\"temperature\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"max_completion_tokens\"", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_ai_provider_omits_temperature_and_uses_max_completion_tokens_for_reasoning_model()
    {
        var handler = new RecordingHandler("""
            { "choices": [{ "message": { "content": "result" } }] }
            """);
        var options = new AiOptions
        {
            BaseUrl = "https://example.test/v1",
            ModelName = "o3",
            ApiKey = "key",
            ReasoningModel = true,
        };

        var provider = Assert.Single(OpenAiCompatibleAiProviderFactory.CreateProviders(options, () => new HttpClient(handler), NullLoggerFactory.Instance));
        await provider.CompleteTextAsync(new AiTextRequest("Summarize", Temperature: 0.1, MaxTokens: 1024));

        Assert.DoesNotContain("\"temperature\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"max_completion_tokens\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"max_tokens\"", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tesseract_step_uses_configured_ocr_options_and_enriches_context()
    {
        var engine = new RecordingOcrEngine(new OcrResult("hello world", "deu", 0.73, ["low confidence"]));
        var step = new TesseractOcrStep(engine, new NoteyOptions
        {
            Ocr = new OcrOptions
            {
                TesseractDataPath = "/configured/tessdata",
                DefaultLanguage = "eng",
            }
        });
        var pipeline = new PipelineDefinition
        {
            Id = "ocr-pipeline",
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "ocr",
                    StepId = TesseractOcrStep.StepTypeId,
                    Configuration = new JsonObject { ["language"] = "deu" },
                },
            ],
        };
        var context = new PipelineContext(pipeline.Id, DateTimeOffset.UtcNow);

        var result = await step.ExecuteAsync(
            new ImageData("screen.png", DateTimeOffset.UtcNow, 200, 100),
            new PipelineStepExecutionContext(pipeline, pipeline.Steps[0], context, null));

        var output = Assert.IsType<OcrTextData>(result.Output);
        Assert.Equal("hello world", output.Text);
        Assert.Equal("deu", engine.LastRequest?.Language);
        Assert.Equal("/configured/tessdata", engine.LastRequest?.DataPath);
        Assert.True(context.TryGetValue<double?>(PipelineStepContextKeys.OcrConfidence, out var confidence));
        Assert.Equal(0.73, confidence);
        Assert.True(context.TryGetValue<int>(PipelineStepContextKeys.OcrWordCount, out var wordCount));
        Assert.Equal(2, wordCount);
        Assert.Contains(context.Warnings, warning => warning.Message == "low confidence" && warning.SourceStepId == "ocr");
    }

    [Fact]
    public async Task Tesseract_step_trims_string_configuration_values()
    {
        var engine = new RecordingOcrEngine(new OcrResult("hello world", "deu", null, []));
        var step = new TesseractOcrStep(engine, new NoteyOptions
        {
            Ocr = new OcrOptions
            {
                DefaultLanguage = "eng",
            }
        });
        var pipeline = new PipelineDefinition
        {
            Id = "ocr-pipeline",
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "ocr",
                    StepId = TesseractOcrStep.StepTypeId,
                    Configuration = new JsonObject { ["language"] = " deu " },
                },
            ],
        };
        var context = new PipelineContext(pipeline.Id, DateTimeOffset.UtcNow);

        await step.ExecuteAsync(
            new ImageData("screen.png", DateTimeOffset.UtcNow, 200, 100),
            new PipelineStepExecutionContext(pipeline, pipeline.Steps[0], context, null));

        Assert.Equal("deu", engine.LastRequest?.Language);
    }

    [Fact]
    public async Task Ai_structured_extraction_accepts_ocr_text_and_preserves_optional_raw_output()
    {
        var provider = new RecordingAiProvider(
            "default",
            """
            { "summary": "Call summary", "people": ["Jane Doe"], "sections": { "Context": "OCR context" } }
            """,
            "gpt-test");
        var step = new AiStructuredExtractionStep(new AiProviderRegistry([provider], "default"));
        var pipeline = new PipelineDefinition
        {
            Id = "ai-pipeline",
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "ai",
                    StepId = AiStructuredExtractionStep.StepTypeId,
                    Configuration = new JsonObject
                    {
                        ["preserveRawOutput"] = true,
                        ["systemPrompt"] = "System",
                        ["taskPrompt"] = "Extract fields",
                        ["modelName"] = "override-model",
                        ["temperature"] = 0.2,
                        ["maxTokens"] = 256,
                    },
                },
            ],
        };
        var context = new PipelineContext(pipeline.Id, DateTimeOffset.UtcNow);

        var result = await step.ExecuteAsync(
            new OcrTextData("OCR text", "screen.png", "eng", 0.8),
            new PipelineStepExecutionContext(pipeline, pipeline.Steps[0], context, null));

        var output = Assert.IsType<StructuredNoteData>(result.Output);
        Assert.Equal("Call summary", output.Summary);
        Assert.NotNull(output.People);
        Assert.Equal("Jane Doe", Assert.Single(output.People).Name);
        Assert.Contains("OCR text", provider.LastRequest?.Prompt, StringComparison.Ordinal);
        Assert.Equal("System", provider.LastRequest?.SystemPrompt);
        Assert.Equal("override-model", provider.LastRequest?.ModelName);
        Assert.True(provider.LastRequest?.JsonOutput);
        Assert.Equal(0.2, provider.LastRequest?.Temperature);
        Assert.Equal(256, provider.LastRequest?.MaxTokens);
        Assert.True(context.TryGetValue<string>(PipelineStepContextKeys.AiProviderId, out var providerId));
        Assert.Equal("default", providerId);
        Assert.True(context.TryGetValue<string>(PipelineStepContextKeys.AiModelName, out var modelName));
        Assert.Equal("gpt-test", modelName);
        Assert.True(context.TryGetValue<string>(PipelineStepContextKeys.AiRawOutput, out var rawOutput));
        Assert.Contains("Call summary", rawOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ai_structured_extraction_surfaces_invalid_json_as_pipeline_error()
    {
        var step = new AiStructuredExtractionStep(new AiProviderRegistry(
            [new RecordingAiProvider("default", "not json", "gpt-test")],
            "default"));
        var pipeline = new PipelineDefinition
        {
            Id = "ai-pipeline",
            Steps =
            [
                new PipelineStepDefinition { Id = "ai", StepId = AiStructuredExtractionStep.StepTypeId },
            ],
        };
        var context = new PipelineContext(pipeline.Id, DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
            await step.ExecuteAsync(
                new TextData("visible text"),
                new PipelineStepExecutionContext(pipeline, pipeline.Steps[0], context, null)));

        Assert.Contains("invalid JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Markdown_assembly_step_outputs_markdown_ready_structured_content()
    {
        var step = new MarkdownAssemblyStep();
        var pipeline = new PipelineDefinition
        {
            Id = "markdown-pipeline",
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "markdown",
                    StepId = MarkdownAssemblyStep.StepTypeId,
                    Configuration = new JsonObject { ["heading"] = "Screenshot context" },
                },
            ],
        };
        var context = new PipelineContext(pipeline.Id, DateTimeOffset.UtcNow);

        var result = await step.ExecuteAsync(
            new StructuredNoteData(
                Summary: "Summarized content.",
                MeetingTitle: "Weekly Sync",
                People: [new EntitySuggestion("Jane Doe", "person")],
                Topics: [new EntitySuggestion("Planning", "topic")],
                Projects: [new EntitySuggestion("Apollo", "project")],
                Tags: ["sync"],
                Sections: new Dictionary<string, string> { ["Actions"] = "- Send notes" }),
            new PipelineStepExecutionContext(pipeline, pipeline.Steps[0], context, null));

        var markdown = Assert.IsType<MarkdownContent>(result.Output);
        Assert.Contains("## Screenshot context", markdown.Markdown, StringComparison.Ordinal);
        Assert.Contains("- Meeting title: Weekly Sync", markdown.Markdown, StringComparison.Ordinal);
        Assert.Contains("- People: Jane Doe", markdown.Markdown, StringComparison.Ordinal);
        Assert.Contains("- Tags: #sync", markdown.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Actions\n- Send notes", markdown.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Tesseract_bundled_languages_includes_eng()
    {
        Assert.Contains("eng", TesseractNativeOcrEngine.BundledLanguages, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tesseract_step_validates_non_bundled_language_without_data_path()
    {
        var step = new TesseractOcrStep(new RecordingOcrEngine(new OcrResult("", "fra", null, [])), new NoteyOptions
        {
            Ocr = new OcrOptions { DefaultLanguage = "fra" }
        });
        var pipeline = new PipelineDefinition
        {
            Id = "ocr-pipeline",
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "ocr",
                    StepId = TesseractOcrStep.StepTypeId,
                },
            ],
        };

        var errors = step.ValidateConfiguration(pipeline.Steps[0]);

        var error = Assert.Single(errors);
        Assert.Contains("fra", error, StringComparison.Ordinal);
        Assert.Contains("TesseractDataPath", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Tesseract_step_does_not_error_for_non_bundled_language_when_data_path_is_configured()
    {
        var step = new TesseractOcrStep(new RecordingOcrEngine(new OcrResult("", "fra", null, [])), new NoteyOptions
        {
            Ocr = new OcrOptions
            {
                DefaultLanguage = "fra",
                TesseractDataPath = "/configured/tessdata",
            }
        });
        var pipeline = new PipelineDefinition
        {
            Id = "ocr-pipeline",
            Steps =
            [
                new PipelineStepDefinition
                {
                    Id = "ocr",
                    StepId = TesseractOcrStep.StepTypeId,
                },
            ],
        };

        var errors = step.ValidateConfiguration(pipeline.Steps[0]);

        Assert.Empty(errors);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            };
        }
    }

    private sealed class RecordingOcrEngine(OcrResult result) : ITesseractOcrEngine
    {
        public TesseractOcrRequest? LastRequest { get; private set; }

        public ValueTask<OcrResult> RecognizeAsync(
            TesseractOcrRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
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
