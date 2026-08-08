namespace Notey.Core.Platform;

public sealed class NoOpImageClipboardService : IImageClipboardService
{
    public ValueTask CopyPngAsync(ReadOnlyMemory<byte> pngBytes, CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException("Image clipboard is not implemented for this platform yet.");
}
