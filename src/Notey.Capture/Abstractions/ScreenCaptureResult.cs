namespace Notey.Capture.Abstractions;

public sealed record ScreenCaptureResult(
    byte[] PngBytes,
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    ScreenCaptureKind Kind);
