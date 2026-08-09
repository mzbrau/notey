namespace Notey.Capture.Abstractions;

public sealed record ScreenCaptureResult(
    byte[] PngBytes,
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    ScreenCaptureKind Kind,
    int ScreenX = 0,
    int ScreenY = 0);
