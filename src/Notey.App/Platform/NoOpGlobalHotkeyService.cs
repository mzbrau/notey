using Microsoft.Extensions.Logging;
using Notey.Core.Platform;

namespace Notey.App.Platform;

public sealed class NoOpGlobalHotkeyService(ILogger<NoOpGlobalHotkeyService> logger) : IGlobalHotkeyService
{
    public ValueTask RegisterAsync(GlobalHotkeyRegistration registration, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Global hotkey {Gesture} for {Name} is registered as a no-op in the foundation phase.", registration.Gesture, registration.Name);
        return ValueTask.CompletedTask;
    }
}
