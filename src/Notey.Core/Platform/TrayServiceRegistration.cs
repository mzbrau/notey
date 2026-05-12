namespace Notey.Core.Platform;

public sealed record TrayServiceRegistration(
    Func<CancellationToken, ValueTask> ShowAsync,
    Func<CancellationToken, ValueTask> NewNoteAsync,
    Func<CancellationToken, ValueTask> ExitAsync);
