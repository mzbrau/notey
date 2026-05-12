using Microsoft.Extensions.Logging;
using Notey.Core.Platform;

namespace Notey.App.Platform;

public sealed class NoOpTrayService(ILogger<NoOpTrayService> logger) : ITrayService
{
    public ValueTask InitializeAsync(TrayServiceRegistration registration, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Tray integration is unavailable on this platform and was registered as a no-op.");
        return ValueTask.CompletedTask;
    }
}
