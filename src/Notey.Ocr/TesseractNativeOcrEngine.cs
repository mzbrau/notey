using TesseractOCR;
using TesseractOCR.Enums;

namespace Notey.Ocr;

public sealed class TesseractNativeOcrEngine : ITesseractOcrEngine
{
    private static readonly SemaphoreSlim _extractLock = new(1, 1);

    public async ValueTask<OcrResult> RecognizeAsync(
        TesseractOcrRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Language);

        if (!File.Exists(request.ImagePath))
        {
            throw new FileNotFoundException("OCR input image was not found.", request.ImagePath);
        }

        var dataPath = !string.IsNullOrWhiteSpace(request.DataPath)
            ? request.DataPath
            : await EnsureTessdataExtractedAsync(request.Language, cancellationToken);

        return await Task.Run(() => RunOcr(request, dataPath), cancellationToken);
    }

    private static OcrResult RunOcr(TesseractOcrRequest request, string dataPath)
    {
        using var engine = new Engine(dataPath, request.Language, EngineMode.Default, null, null, false, null);
        using var img = TesseractOCR.Pix.Image.LoadFromFile(request.ImagePath);
        using var page = engine.Process(img);

        var text = page.Text ?? string.Empty;
        var confidence = (double?)page.MeanConfidence;
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            warnings.Add("Tesseract completed but returned no text.");
        }

        return new OcrResult(text.Trim(), request.Language, confidence, warnings);
    }

    private static async ValueTask<string> EnsureTessdataExtractedAsync(
        string language,
        CancellationToken cancellationToken)
    {
        var tessDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Notey",
            "tessdata");

        var trainedDataPath = Path.Combine(tessDir, $"{language}.traineddata");
        if (File.Exists(trainedDataPath))
        {
            return tessDir;
        }

        await _extractLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check after acquiring the lock in case another thread already extracted.
            if (File.Exists(trainedDataPath))
            {
                return tessDir;
            }

            Directory.CreateDirectory(tessDir);

            var resourceName = $"Notey.Ocr.tessdata.{language}.traineddata";
            using var resourceStream = typeof(TesseractNativeOcrEngine).Assembly
                .GetManifestResourceStream(resourceName);

            if (resourceStream is null)
            {
                throw new InvalidOperationException(
                    $"Bundled tessdata for language '{language}' not found. " +
                    $"Set Notey:Ocr:TesseractDataPath to a directory containing '{language}.traineddata'.");
            }

            var tempPath = trainedDataPath + ".tmp";
            try
            {
                using (var file = File.Create(tempPath))
                {
                    await resourceStream.CopyToAsync(file, cancellationToken);
                }

                File.Move(tempPath, trainedDataPath, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                throw;
            }
        }
        finally
        {
            _extractLock.Release();
        }

        return tessDir;
    }
}
