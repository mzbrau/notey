namespace Notey.Capture.Abstractions;

public sealed record ScreenSnipResult(
    string FilePath,
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    ScreenSnipMode Mode);
