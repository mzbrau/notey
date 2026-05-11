namespace Notey.AI.Abstractions;

public interface IScreenshotAnalysisService
{
    ValueTask<ScreenshotAnalysisResult> AnalyzeAsync(ScreenshotAnalysisRequest request, CancellationToken cancellationToken = default);
}
