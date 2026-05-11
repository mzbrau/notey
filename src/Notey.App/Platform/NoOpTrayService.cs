using Microsoft.Extensions.Logging;
using Notey.Core.Platform;

namespace Notey.App.Platform;

public sealed class NoOpTrayService(ILogger<NoOpTrayService> logger) : ITrayService
{
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Tray integration is registered as a no-op in the foundation phase.");
        return ValueTask.CompletedTask;
    }
}
