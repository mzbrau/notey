namespace Notey.Core.Platform;

public interface IImageClipboardService
{
    ValueTask CopyPngAsync(ReadOnlyMemory<byte> pngBytes, CancellationToken cancellationToken = default);
}
