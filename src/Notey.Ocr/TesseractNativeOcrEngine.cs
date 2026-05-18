using System.Reflection;
using TesseractOCR;
using TesseractOCR.Enums;

namespace Notey.Ocr;

public sealed class TesseractNativeOcrEngine : ITesseractOcrEngine
{
    private static readonly SemaphoreSlim _extractLock = new(1, 1);

    /// <summary>
    /// Language codes whose <c>.traineddata</c> files are embedded in this assembly.
    /// If a user configures <c>Notey:Ocr:DefaultLanguage</c> to a language that is not
    /// in this set and does not also supply a <c>Notey:Ocr:TesseractDataPath</c>, OCR will
    /// fail at runtime.
    /// </summary>
    public static IReadOnlySet<string> BundledLanguages { get; } =
        typeof(TesseractNativeOcrEngine).Assembly
            .GetManifestResourceNames()
            .Where(static n => n.StartsWith("Notey.Ocr.tessdata.", StringComparison.Ordinal)
                            && n.EndsWith(".traineddata", StringComparison.Ordinal))
            .Select(static n => n["Notey.Ocr.tessdata.".Length..^".traineddata".Length])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

        var dataPath = await ResolveDataPathAsync(request.DataPath, request.Language, cancellationToken);

        try
        {
            return await Task.Run(() => RunOcr(request, dataPath), cancellationToken);
        }
        catch (Exception ex) when (IsNativeDependencyLoadFailure(ex))
        {
            throw new OcrDependencyUnavailableException(
                "Tesseract OCR native libraries are not available for this platform. Install the required Tesseract and Leptonica native dependencies to enable OCR.",
                ex);
        }
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

    private static bool IsNativeDependencyLoadFailure(Exception exception)
    {
        return exception switch
        {
            DllNotFoundException => true,
            EntryPointNotFoundException => true,
            BadImageFormatException => true,
            TargetInvocationException { InnerException: { } innerException } => IsNativeDependencyLoadFailure(innerException),
            TypeInitializationException { InnerException: { } innerException } => IsNativeDependencyLoadFailure(innerException),
            _ => false
        };
    }

    private static async ValueTask<string> ResolveDataPathAsync(
        string? configuredDataPath,
        string language,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredDataPath))
        {
            var expectedFile = Path.Combine(configuredDataPath, $"{language}.traineddata");
            if (File.Exists(expectedFile))
            {
                return configuredDataPath;
            }

            // Configured path does not contain the language file — fall back to bundled tessdata.
        }

        return await EnsureTessdataExtractedAsync(language, cancellationToken);
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
