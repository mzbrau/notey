namespace Notey.Core.Platform;

public interface IGlobalHotkeyService
{
    ValueTask RegisterAsync(GlobalHotkeyRegistration registration, CancellationToken cancellationToken = default);
}
