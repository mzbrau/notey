namespace Notey.Core.Platform;

public sealed record GlobalHotkeyRegistration(
    string Name,
    string Gesture,
    Func<CancellationToken, ValueTask> ActivatedAsync);
