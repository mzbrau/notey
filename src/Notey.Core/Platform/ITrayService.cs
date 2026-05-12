namespace Notey.Core.Platform;

public interface ITrayService
{
    ValueTask InitializeAsync(TrayServiceRegistration registration, CancellationToken cancellationToken = default);
}
