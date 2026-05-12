using System.Text;
using Notey.Core.Notes;
using Notey.Vault.Abstractions;

namespace Notey.Vault.Notes;

public sealed class FileSystemNoteDraftStore(
    IVaultWorkspace workspace,
    NoteTemplateFactory templateFactory,
    NoteFileNameGenerator fileNameGenerator) : INoteDraftStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task<NoteDraft> CreateAsync(DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var notesPath = workspace.GetPaths().NotesPath;
        Directory.CreateDirectory(notesPath);

        var content = templateFactory.Create(createdAt);
        var fileName = fileNameGenerator.Generate(createdAt);

        for (var index = 1; index < int.MaxValue; index++)
        {
            var filePath = GetCandidateFilePath(notesPath, fileName, index);

            if (await TryCreateDraftFileAsync(filePath, content, cancellationToken))
            {
                return new NoteDraft(filePath, content, createdAt);
            }
        }

        throw new InvalidOperationException("Unable to generate a unique note filename.");
    }

    public async Task SaveAsync(NoteDraft draft, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(draft.FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Draft file path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        await WriteUtf8AtomicallyAsync(draft.FilePath, content, cancellationToken);
    }

    private static string GetCandidateFilePath(string directory, string fileName, int index)
    {
        if (index == 1)
        {
            return Path.Combine(directory, fileName);
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        return Path.Combine(directory, $"{baseName}-{index}{extension}");
    }

    private static async Task<bool> TryCreateDraftFileAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        FileStream stream;

        try
        {
            stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
        }
        catch (IOException) when (File.Exists(filePath))
        {
            return false;
        }

        try
        {
            await using (stream)
            {
                await WriteUtf8Async(stream, content, cancellationToken);
            }
        }
        catch
        {
            DeleteIfExists(filePath);
            throw;
        }

        return true;
    }

    private static async Task WriteUtf8AtomicallyAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Draft file path must include a directory.");
        }

        var tempFilePath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteUtf8Async(tempFilePath, content, FileMode.CreateNew, cancellationToken);
            File.Move(tempFilePath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private static async Task WriteUtf8Async(string filePath, string content, FileMode fileMode, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, fileMode, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
        await WriteUtf8Async(stream, content, cancellationToken);
    }

    private static async Task WriteUtf8Async(Stream stream, string content, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, Utf8NoBom);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
