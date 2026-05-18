namespace Notey.Ocr;

public interface ITesseractOcrEngine
{
    ValueTask<OcrResult> RecognizeAsync(TesseractOcrRequest request, CancellationToken cancellationToken = default);
}

public sealed record TesseractOcrRequest(
    string ImagePath,
    string Language,
    string? DataPath = null);

public sealed record OcrResult(
    string Text,
    string Language,
    double? Confidence,
    IReadOnlyList<string> Warnings);
