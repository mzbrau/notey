using Microsoft.Extensions.Logging.Abstractions;
using Notey.Pipelines.Catalog;
using Notey.Pipelines.Context;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Execution;
using Notey.Pipelines.Progress;
using Notey.Pipelines.Registry;
using Notey.Pipelines.Steps;
using Notey.Pipelines.Validation;

namespace Notey.Tests;

public sealed class PipelineFoundationTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task ExecuteAsync_runs_valid_chain_and_preserves_context_enrichment()
    {
        var registry = CreateRegistry(new OcrStep(), new StructuredExtractionStep());
        var pipeline = CreateImageToStructuredPipeline();
        var executor = CreateExecutor(registry);
        var progress = new RecordingProgress<PipelineProgressUpdate>();
        var context = new PipelineContext(pipeline.Id, DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(
            pipeline,
            new ImageData("meeting.png", DateTimeOffset.UtcNow, 1200, 800),
            context,
            progress);

        var output = Assert.IsType<StructuredNoteData>(result.Output);
        Assert.Equal("OCR text from meeting.png", output.Summary);
        Assert.True(result.Context.TryGetValue<string>("ocr.language", out var language));
        Assert.Equal("eng", language);
        Assert.Contains(result.Context.Warnings, warning => warning.SourceStepId == "ocr");
        Assert.Contains(progress.Updates, update => update.Status == PipelineProgressStatus.Started);
        Assert.Contains(progress.Updates, update => update.Status == PipelineProgressStatus.Completed);
    }

    [Fact]
    public void Validate_rejects_incompatible_step_chain()
    {
        var registry = CreateRegistry(new StructuredExtractionStep());
        var validator = new PipelineValidator(registry);
        var pipeline = new PipelineDefinition
        {
            Id = "bad-chain",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.StructuredNoteData,
            Steps =
            [
                new PipelineStepDefinition { Id = "extract", StepId = StructuredExtractionStep.StepTypeId },
            ],
        };

        var result = validator.Validate(pipeline);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("cannot consume ImageData", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Multi_input_step_can_accept_text_or_ocr_text()
    {
        var registry = CreateRegistry(new MarkdownAssemblyStep());
        var validator = new PipelineValidator(registry);
        var pipeline = new PipelineDefinition
        {
            Id = "text-to-markdown",
            AcceptedInputTypes = [PipelineDataType.TextData, PipelineDataType.OcrTextData],
            FinalOutputType = PipelineDataType.MarkdownContent,
            Steps =
            [
                new PipelineStepDefinition { Id = "assemble", StepId = MarkdownAssemblyStep.StepTypeId },
            ],
        };

        var validation = validator.Validate(pipeline);
        var result = await CreateExecutor(registry).ExecuteAsync(
            pipeline,
            new OcrTextData("visible agenda", "snip", "eng", 0.92));

        var markdown = Assert.IsType<MarkdownContent>(result.Output);
        Assert.True(validation.IsValid);
        Assert.Equal("## AI context\n\nvisible agenda", markdown.Markdown);
    }

    [Fact]
    public void Validate_rejects_non_note_ready_final_output()
    {
        var registry = CreateRegistry(new ImageToTextStep());
        var validator = new PipelineValidator(registry);
        var pipeline = new PipelineDefinition
        {
            Id = "text-final",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.TextData,
            Steps =
            [
                new PipelineStepDefinition { Id = "text", StepId = ImageToTextStep.StepTypeId },
            ],
        };

        var result = validator.Validate(pipeline);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("final output type must be StructuredNoteData or MarkdownContent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Catalog_returns_only_enabled_valid_compatible_pipelines()
    {
        var rootPath = CreateTempDirectory();
        var configPath = Path.Combine(rootPath, "pipelines.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "pipelines": [
                {
                  "id": "enabled-valid",
                  "displayName": "Enabled valid",
                  "enabled": true,
                  "acceptedInputTypes": [ "ImageData" ],
                  "steps": [ { "id": "markdown", "stepId": "image-to-markdown" } ],
                  "finalOutputType": "MarkdownContent"
                },
                {
                  "id": "disabled-valid",
                  "displayName": "Disabled valid",
                  "enabled": false,
                  "acceptedInputTypes": [ "ImageData" ],
                  "steps": [ { "id": "markdown", "stepId": "image-to-markdown" } ],
                  "finalOutputType": "MarkdownContent"
                },
                {
                  "id": "unknown-step",
                  "displayName": "Unknown step",
                  "enabled": true,
                  "acceptedInputTypes": [ "ImageData" ],
                  "steps": [ { "id": "missing", "stepId": "does-not-exist" } ],
                  "finalOutputType": "MarkdownContent"
                }
              ]
            }
            """);
        var registry = CreateRegistry(new ImageToMarkdownStep());
        var catalog = new PipelineCatalog(new FilePipelineDefinitionSource(configPath), new PipelineValidator(registry));

        var compatiblePipelines = await catalog.GetEnabledCompatibleAsync(PipelineDataType.ImageData);
        var snapshot = await catalog.LoadAsync();

        var pipeline = Assert.Single(compatiblePipelines);
        Assert.Equal("enabled-valid", pipeline.Id);
        Assert.True(snapshot.ValidationResults["disabled-valid"].IsValid);
        Assert.False(snapshot.ValidationResults["unknown-step"].IsValid);
        Assert.Contains(
            snapshot.ValidationResults["unknown-step"].Errors,
            error => error.Contains("unknown step id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task File_source_reports_warning_when_pipeline_file_is_missing()
    {
        var rootPath = CreateTempDirectory();
        var source = new FilePipelineDefinitionSource(Path.Combine(rootPath, "missing-pipelines.json"));

        var result = await source.LoadAsync();

        Assert.Empty(result.Definitions);
        Assert.Contains(result.Warnings, warning => warning.Contains("was not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task File_source_reports_warning_when_pipeline_json_is_malformed()
    {
        var rootPath = CreateTempDirectory();
        var configPath = Path.Combine(rootPath, "pipelines.json");
        await File.WriteAllTextAsync(configPath, "{ not json");
        var source = new FilePipelineDefinitionSource(configPath);

        var result = await source.LoadAsync();

        Assert.Empty(result.Definitions);
        Assert.Contains(result.Warnings, warning => warning.Contains("could not be loaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Catalog_reports_duplicate_pipeline_ids_without_throwing()
    {
        var rootPath = CreateTempDirectory();
        var configPath = Path.Combine(rootPath, "pipelines.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "pipelines": [
                {
                  "id": "duplicate",
                  "enabled": true,
                  "acceptedInputTypes": [ "ImageData" ],
                  "steps": [ { "id": "markdown", "stepId": "image-to-markdown" } ],
                  "finalOutputType": "MarkdownContent"
                },
                {
                  "id": "Duplicate",
                  "enabled": true,
                  "acceptedInputTypes": [ "ImageData" ],
                  "steps": [ { "id": "markdown", "stepId": "image-to-markdown" } ],
                  "finalOutputType": "MarkdownContent"
                }
              ]
            }
            """);
        var catalog = new PipelineCatalog(
            new FilePipelineDefinitionSource(configPath),
            new PipelineValidator(CreateRegistry(new ImageToMarkdownStep())));

        var snapshot = await catalog.LoadAsync();
        var compatiblePipelines = await catalog.GetEnabledCompatibleAsync(PipelineDataType.ImageData);

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Contains(snapshot.LoadWarnings, warning => warning.Contains("duplicated", StringComparison.Ordinal));
        Assert.Empty(compatiblePipelines);
    }

    [Fact]
    public async Task File_source_reports_null_pipeline_entries_as_warnings()
    {
        var rootPath = CreateTempDirectory();
        var configPath = Path.Combine(rootPath, "pipelines.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "pipelines": [ null ]
            }
            """);
        var source = new FilePipelineDefinitionSource(configPath);

        var result = await source.LoadAsync();

        Assert.Empty(result.Definitions);
        Assert.Contains(result.Warnings, warning => warning.Contains("is null and was ignored", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_null_step_entries_without_throwing()
    {
        var validator = new PipelineValidator(CreateRegistry());
        var pipeline = new PipelineDefinition
        {
            Id = "null-step",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.MarkdownContent,
            Steps = [null!],
        };

        var result = validator.Validate(pipeline);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("null step definition", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_missing_step_sequence()
    {
        var validator = new PipelineValidator(CreateRegistry());
        var pipeline = new PipelineDefinition
        {
            Id = "missing-steps",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.MarkdownContent,
            Steps = [],
        };

        var result = validator.Validate(pipeline);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("must declare at least one step", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generic_step_preserves_progress_message()
    {
        var registry = CreateRegistry(new GenericImageToMarkdownStep());
        var pipeline = new PipelineDefinition
        {
            Id = "generic-message",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.MarkdownContent,
            Steps =
            [
                new PipelineStepDefinition { Id = "markdown", StepId = GenericImageToMarkdownStep.StepTypeId },
            ],
        };
        var progress = new RecordingProgress<PipelineProgressUpdate>();

        var result = await CreateExecutor(registry).ExecuteAsync(
            pipeline,
            new ImageData("meeting.png", DateTimeOffset.UtcNow, 1200, 800),
            progress: progress);

        Assert.IsType<MarkdownContent>(result.Output);
        Assert.Contains(progress.Updates, update => update.Message == "Markdown assembled.");
    }

    [Fact]
    public void Generic_step_rejects_mismatched_enum_and_clr_contracts()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new MismatchedGenericStep());

        Assert.Contains("CLR input type is OcrTextData", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_validates_blank_pipeline_id_before_creating_context()
    {
        var pipeline = new PipelineDefinition
        {
            Id = "",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.MarkdownContent,
            Steps =
            [
                new PipelineStepDefinition { Id = "markdown", StepId = ImageToMarkdownStep.StepTypeId },
            ],
        };

        var exception = await Assert.ThrowsAsync<PipelineValidationException>(async () =>
            await CreateExecutor(CreateRegistry(new ImageToMarkdownStep())).ExecuteAsync(
                pipeline,
                new ImageData("meeting.png", DateTimeOffset.UtcNow, 1200, 800)));

        Assert.Contains("Pipeline id is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_reports_pipeline_execution_exception_messages_in_progress()
    {
        var registry = CreateRegistry(new WrongOutputStep());
        var pipeline = new PipelineDefinition
        {
            Id = "wrong-output",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.MarkdownContent,
            Steps =
            [
                new PipelineStepDefinition { Id = "wrong", StepId = WrongOutputStep.StepTypeId },
            ],
        };
        var progress = new RecordingProgress<PipelineProgressUpdate>();

        await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
            await CreateExecutor(registry).ExecuteAsync(
                pipeline,
                new ImageData("meeting.png", DateTimeOffset.UtcNow, 1200, 800),
                progress: progress));

        Assert.Contains(progress.Updates, update =>
            update.Status == PipelineProgressStatus.Failed &&
            update.Message?.Contains("declared MarkdownContent but returned TextData", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Generic_multi_input_step_rejects_direct_calls_with_unaccepted_data_type()
    {
        var step = new GenericTextToMarkdownStep();
        var pipeline = new PipelineDefinition
        {
            Id = "text-only",
            AcceptedInputTypes = [PipelineDataType.TextData],
            FinalOutputType = PipelineDataType.MarkdownContent,
            Steps =
            [
                new PipelineStepDefinition { Id = "text", StepId = GenericTextToMarkdownStep.StepTypeId },
            ],
        };
        var context = new PipelineContext(pipeline.Id, DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
            await step.ExecuteAsync(
                new ImageData("meeting.png", DateTimeOffset.UtcNow, 1200, 800),
                new PipelineStepExecutionContext(pipeline, pipeline.Steps[0], context, null)));

        Assert.Contains("cannot consume runtime input type ImageData", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static PipelineDefinition CreateImageToStructuredPipeline()
    {
        return new PipelineDefinition
        {
            Id = "screen-to-structured",
            DisplayName = "Screen to structured note",
            AcceptedInputTypes = [PipelineDataType.ImageData],
            FinalOutputType = PipelineDataType.StructuredNoteData,
            Steps =
            [
                new PipelineStepDefinition { Id = "ocr", StepId = OcrStep.StepTypeId },
                new PipelineStepDefinition { Id = "extract", StepId = StructuredExtractionStep.StepTypeId },
            ],
        };
    }

    private static PipelineExecutor CreateExecutor(IPipelineStepRegistry registry)
    {
        return new PipelineExecutor(registry, new PipelineValidator(registry), TimeProvider.System, NullLogger<PipelineExecutor>.Instance);
    }

    private static PipelineStepRegistry CreateRegistry(params IPipelineStep[] steps)
    {
        return new PipelineStepRegistry(steps);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "notey-pipelines-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private sealed class OcrStep : TestStep
    {
        public const string StepTypeId = "ocr";

        public OcrStep()
            : base(StepTypeId, new HashSet<PipelineDataType> { PipelineDataType.ImageData }, PipelineDataType.OcrTextData)
        {
        }

        protected override ValueTask<PipelineData> ExecuteCoreAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            var image = Assert.IsType<ImageData>(input);
            context.Context.SetValue("ocr.language", "eng");
            context.Context.AddWarning("Low contrast detected.", context.Step.Id);
            return ValueTask.FromResult<PipelineData>(
                new OcrTextData($"OCR text from {image.FilePath}", image.FilePath, "eng", 0.86));
        }
    }

    private sealed class StructuredExtractionStep : TestStep
    {
        public const string StepTypeId = "structured-extraction";

        public StructuredExtractionStep()
            : base(
                StepTypeId,
                new HashSet<PipelineDataType> { PipelineDataType.TextData, PipelineDataType.OcrTextData },
                PipelineDataType.StructuredNoteData)
        {
        }

        protected override ValueTask<PipelineData> ExecuteCoreAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            var text = input switch
            {
                TextData textData => textData.Text,
                OcrTextData ocrTextData => ocrTextData.Text,
                _ => throw new InvalidOperationException("Unsupported test input."),
            };

            return ValueTask.FromResult<PipelineData>(new StructuredNoteData(Summary: text));
        }
    }

    private sealed class MarkdownAssemblyStep : TestStep
    {
        public const string StepTypeId = "markdown-assembly";

        public MarkdownAssemblyStep()
            : base(
                StepTypeId,
                new HashSet<PipelineDataType> { PipelineDataType.TextData, PipelineDataType.OcrTextData },
                PipelineDataType.MarkdownContent)
        {
        }

        protected override ValueTask<PipelineData> ExecuteCoreAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            var text = input switch
            {
                TextData textData => textData.Text,
                OcrTextData ocrTextData => ocrTextData.Text,
                _ => throw new InvalidOperationException("Unsupported test input."),
            };

            return ValueTask.FromResult<PipelineData>(new MarkdownContent($"## AI context\n\n{text}"));
        }
    }

    private sealed class ImageToTextStep : TestStep
    {
        public const string StepTypeId = "image-to-text";

        public ImageToTextStep()
            : base(StepTypeId, new HashSet<PipelineDataType> { PipelineDataType.ImageData }, PipelineDataType.TextData)
        {
        }

        protected override ValueTask<PipelineData> ExecuteCoreAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<PipelineData>(new TextData("text"));
        }
    }

    private sealed class ImageToMarkdownStep : TestStep
    {
        public const string StepTypeId = "image-to-markdown";

        public ImageToMarkdownStep()
            : base(StepTypeId, new HashSet<PipelineDataType> { PipelineDataType.ImageData }, PipelineDataType.MarkdownContent)
        {
        }

        protected override ValueTask<PipelineData> ExecuteCoreAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<PipelineData>(new MarkdownContent("![snip](meeting.png)"));
        }
    }

    private sealed class GenericImageToMarkdownStep : PipelineStep<ImageData, MarkdownContent>
    {
        public const string StepTypeId = "generic-image-to-markdown";

        public GenericImageToMarkdownStep()
            : base(PipelineDataType.ImageData)
        {
        }

        public override string Id => StepTypeId;

        public override string DisplayName => "Generic image to markdown";

        protected override ValueTask<PipelineStepResult<MarkdownContent>> ExecuteTypedAsync(
            ImageData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new PipelineStepResult<MarkdownContent>(
                new MarkdownContent($"![[{input.FilePath}]]"),
                "Markdown assembled."));
        }
    }

    private sealed class MismatchedGenericStep : PipelineStep<OcrTextData, MarkdownContent>
    {
        public MismatchedGenericStep()
            : base(PipelineDataType.TextData)
        {
        }

        public override string Id => "mismatched";

        public override string DisplayName => "Mismatched";

        protected override ValueTask<PipelineStepResult<MarkdownContent>> ExecuteTypedAsync(
            OcrTextData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new PipelineStepResult<MarkdownContent>(new MarkdownContent(input.Text)));
        }
    }

    private sealed class GenericTextToMarkdownStep : PipelineStep<PipelineData, MarkdownContent>
    {
        public const string StepTypeId = "generic-text-to-markdown";

        public GenericTextToMarkdownStep()
            : base(new HashSet<PipelineDataType> { PipelineDataType.TextData, PipelineDataType.OcrTextData })
        {
        }

        public override string Id => StepTypeId;

        public override string DisplayName => "Generic text to markdown";

        protected override ValueTask<PipelineStepResult<MarkdownContent>> ExecuteTypedAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            var text = input switch
            {
                TextData textData => textData.Text,
                OcrTextData ocrTextData => ocrTextData.Text,
                _ => throw new InvalidOperationException("Unsupported test input."),
            };

            return ValueTask.FromResult(new PipelineStepResult<MarkdownContent>(new MarkdownContent(text)));
        }
    }

    private sealed class WrongOutputStep : TestStep
    {
        public const string StepTypeId = "wrong-output";

        public WrongOutputStep()
            : base(StepTypeId, new HashSet<PipelineDataType> { PipelineDataType.ImageData }, PipelineDataType.MarkdownContent)
        {
        }

        protected override ValueTask<PipelineData> ExecuteCoreAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<PipelineData>(new TextData("not markdown"));
        }
    }

    private abstract class TestStep(
        string id,
        IReadOnlySet<PipelineDataType> acceptedInputTypes,
        PipelineDataType outputType) : IPipelineStep
    {
        public string Id { get; } = id;

        public string DisplayName => Id;

        public IReadOnlySet<PipelineDataType> AcceptedInputTypes { get; } = acceptedInputTypes;

        public PipelineDataType OutputType { get; } = outputType;

        public IReadOnlyList<string> ValidateConfiguration(PipelineStepDefinition definition) => [];

        public async ValueTask<PipelineStepResult> ExecuteAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return new PipelineStepResult(await ExecuteCoreAsync(input, context, cancellationToken));
        }

        protected abstract ValueTask<PipelineData> ExecuteCoreAsync(
            PipelineData input,
            PipelineStepExecutionContext context,
            CancellationToken cancellationToken);
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        private readonly List<T> _updates = [];

        public IReadOnlyList<T> Updates => _updates;

        public void Report(T value)
        {
            _updates.Add(value);
        }
    }
}
