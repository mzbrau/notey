namespace Notey.App.Imports;

public sealed class ImportFile(
    string fileName,
    Func<CancellationToken, ValueTask<Stream>> openReadAsync)
{
    public string FileName { get; } = string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName;

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        return openReadAsync(cancellationToken);
    }

    public static ImportFile FromFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return new ImportFile(
            Path.GetFileName(filePath),
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
                return ValueTask.FromResult(stream);
            });
    }

    public static ImportFile FromBytes(string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return new ImportFile(
            fileName,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stream stream = new MemoryStream(bytes, writable: false);
                return ValueTask.FromResult(stream);
            });
    }
}
