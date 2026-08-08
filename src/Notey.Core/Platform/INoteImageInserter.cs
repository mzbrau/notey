namespace Notey.Core.Platform;

public interface INoteImageInserter
{
    ValueTask InsertPngImageAsync(ReadOnlyMemory<byte> pngBytes, CancellationToken cancellationToken = default);
}
