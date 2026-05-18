namespace Notey.Ocr;

public sealed class OcrDependencyUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
