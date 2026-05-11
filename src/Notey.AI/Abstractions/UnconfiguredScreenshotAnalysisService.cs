namespace Notey.AI.Abstractions;

public sealed class UnconfiguredScreenshotAnalysisService : IScreenshotAnalysisService
{
    public ValueTask<ScreenshotAnalysisResult> AnalyzeAsync(ScreenshotAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Screenshot analysis requires an OpenAI-compatible API configuration.");
    }
}
