namespace Notey.Core.Platform;

public interface ITrayService
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);
}
