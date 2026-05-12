namespace Notey.Capture.Abstractions;

public interface IScreenSnipService
{
    ValueTask<ScreenSnipResult> CaptureAsync(ScreenSnipMode mode, CancellationToken cancellationToken = default);
}
