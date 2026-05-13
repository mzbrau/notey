using Microsoft.Extensions.Logging;
using Notey.Core.Platform;

namespace Notey.App.Platform;

public sealed class NoOpGlobalHotkeyService(ILogger<NoOpGlobalHotkeyService> logger) : IGlobalHotkeyService
{
    public ValueTask RegisterAsync(GlobalHotkeyRegistration registration, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Global hotkey {Gesture} for {Name} is unavailable on this platform and was registered as a no-op.", registration.Gesture, registration.Name);
        return ValueTask.CompletedTask;
    }

    public ValueTask UnregisterAllAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
