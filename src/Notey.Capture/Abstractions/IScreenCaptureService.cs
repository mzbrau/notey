namespace Notey.Capture.Abstractions;

public interface IScreenCaptureService
{
    ValueTask<ScreenCaptureResult> CaptureFullScreenAsync(CancellationToken cancellationToken = default);

    ValueTask<ScreenCaptureResult> CaptureRegionAsync(CancellationToken cancellationToken = default);

    ValueTask<ScreenCaptureResult> CaptureWindowAsync(CancellationToken cancellationToken = default);
}
