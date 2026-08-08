namespace Notey.Capture.Abstractions;

public sealed class UnavailableScreenCaptureService : IScreenCaptureService
{
    public ValueTask<ScreenCaptureResult> CaptureFullScreenAsync(CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException("Screen capture is not implemented for this platform yet.");

    public ValueTask<ScreenCaptureResult> CaptureRegionAsync(CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException("Screen capture is not implemented for this platform yet.");

    public ValueTask<ScreenCaptureResult> CaptureWindowAsync(CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException("Screen capture is not implemented for this platform yet.");
}
