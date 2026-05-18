using Notey.Core.Configuration;
using Notey.Ocr;
using Notey.Pipelines.Data;
using Notey.Pipelines.Definitions;
using Notey.Pipelines.Steps;

namespace Notey.PipelineSteps;

public sealed class TesseractOcrStep(
    ITesseractOcrEngine ocrEngine,
    NoteyOptions options) : PipelineStep<ImageData, OcrTextData>(PipelineDataType.ImageData)
{
    public const string StepTypeId = "tesseract-ocr";

    public override string Id => StepTypeId;

    public override string DisplayName => "Tesseract OCR";

    public override IReadOnlyList<string> ValidateConfiguration(PipelineStepDefinition definition)
    {
        var errors = new List<string>();
        var language = GetLanguage(definition);

        if (string.IsNullOrWhiteSpace(language))
        {
            errors.Add("language must be configured either on the step or in Notey:Ocr:DefaultLanguage.");
        }

        return errors;
    }

    protected override async ValueTask<PipelineStepResult<OcrTextData>> ExecuteTypedAsync(
        ImageData input,
        PipelineStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        var language = GetLanguage(context.Step);
        var result = await ocrEngine.RecognizeAsync(
            new TesseractOcrRequest(
                input.FilePath,
                language,
                GetDataPath(context.Step)),
            cancellationToken);

        context.Context.SetValue(PipelineStepContextKeys.OcrLanguage, result.Language);
        context.Context.SetValue(PipelineStepContextKeys.OcrConfidence, result.Confidence);
        context.Context.SetValue(PipelineStepContextKeys.OcrSourceImagePath, input.FilePath);
        context.Context.SetValue(PipelineStepContextKeys.OcrWordCount, CountWords(result.Text));
        foreach (var warning in result.Warnings)
        {
            context.Context.AddWarning(warning, context.Step.Id);
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            context.Context.AddWarning("Tesseract completed but did not return OCR text.", context.Step.Id);
        }

        return new PipelineStepResult<OcrTextData>(
            new OcrTextData(result.Text, input.FilePath, result.Language, result.Confidence),
            string.IsNullOrWhiteSpace(result.Text) ? "No OCR text detected." : "OCR text extracted.");
    }

    private string GetLanguage(PipelineStepDefinition definition)
    {
        return StepConfigurationReader.GetString(definition.Configuration, "language")
            ?? options.Ocr.DefaultLanguage;
    }

    private string? GetDataPath(PipelineStepDefinition definition)
    {
        return StepConfigurationReader.GetString(definition.Configuration, "dataPath")
            ?? (string.IsNullOrWhiteSpace(options.Ocr.TesseractDataPath) ? null : options.Ocr.TesseractDataPath);
    }

    private static int CountWords(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
