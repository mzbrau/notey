namespace Notey.Capture.Abstractions;

public sealed class UnavailableScreenSnipService : IScreenSnipService
{
    public ValueTask<ScreenSnipResult> CaptureAsync(ScreenSnipMode mode, CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("Screen snipping is not implemented for this platform yet.");
    }
}
