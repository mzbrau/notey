using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Notey.Core.Notes;
using Notey.Vault.Abstractions;

namespace Notey.Vault.Notes;

public sealed partial class FileSystemNoteDraftStore(
    IVaultWorkspace workspace,
    NoteTemplateFactory templateFactory,
    NoteFileNameGenerator fileNameGenerator) : INoteDraftStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task<NoteDraft> CreateAsync(DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var notesPath = workspace.GetPaths().DraftPath;
        Directory.CreateDirectory(notesPath);

        var normalizedCreatedAt = TruncateToMinute(createdAt);
        var content = templateFactory.Create(normalizedCreatedAt);
        var fileName = fileNameGenerator.Generate(normalizedCreatedAt);

        for (var index = 1; index < int.MaxValue; index++)
        {
            var filePath = GetCandidateFilePath(notesPath, fileName, index);

            if (await TryCreateDraftFileAsync(filePath, content, cancellationToken))
            {
                return new NoteDraft(filePath, content, normalizedCreatedAt);
            }
        }

        throw new InvalidOperationException("Unable to generate a unique note filename.");
    }

    public async Task<NoteDraft> OpenAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var notesPath = workspace.GetPaths().DraftPath;
        var fullFilePath = Path.GetFullPath(filePath);
        EnsureFileIsInsideNotesPath(notesPath, fullFilePath);

        var content = await File.ReadAllTextAsync(fullFilePath, cancellationToken);
        var createdAt = TryReadCreatedAt(content)
            ?? TryReadCreatedAtFromFileName(fullFilePath)
            ?? GetFileCreatedAt(fullFilePath);

        return new NoteDraft(fullFilePath, content, createdAt);
    }

    public async Task<NoteDraft?> FindMostRecentAsync(DateTimeOffset createdAfter, CancellationToken cancellationToken = default)
    {
        var recent = await ListRecentAsync(createdAfter, cancellationToken);
        return recent.Count == 0
            ? null
            : await OpenAsync(recent[0].FilePath, cancellationToken);
    }

    public async Task<IReadOnlyList<RecentNoteSummary>> ListRecentAsync(DateTimeOffset createdAfter, CancellationToken cancellationToken = default)
    {
        var notesPath = workspace.GetPaths().DraftPath;
        if (!Directory.Exists(notesPath))
        {
            return [];
        }

        var recent = new List<RecentNoteSummary>();

        foreach (var filePath in Directory.EnumerateFiles(notesPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var summary = await ReadRecentSummaryAsync(filePath, cancellationToken);
            if (summary.CreatedAt >= createdAfter)
            {
                recent.Add(summary);
            }
        }

        return RecentNotes.OrderByMostRecent(recent);
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

    public async Task DeleteEmptyDraftsAsync(CancellationToken cancellationToken = default)
    {
        var notesPath = workspace.GetPaths().DraftPath;
        if (!Directory.Exists(notesPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(notesPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                DeleteIfExists(filePath);
                DeleteDraftAssetsIfExists(filePath);
            }
        }

        var activeDraftAssets = Directory
            .EnumerateFiles(notesPath, "*.md", SearchOption.TopDirectoryOnly)
            .Select(GetDraftAssetsDirectory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var assetsDirectory in Directory.EnumerateDirectories(notesPath, "*.assets", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!activeDraftAssets.Contains(assetsDirectory))
            {
                TryDeleteDirectory(assetsDirectory);
            }
        }
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

    private static async Task<RecentNoteSummary> ReadRecentSummaryAsync(string filePath, CancellationToken cancellationToken)
    {
        const int previewLength = 8192;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var buffer = new char[previewLength];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(0, previewLength), cancellationToken);
        var preview = new string(buffer, 0, read);
        var createdAt = TryReadCreatedAt(preview)
            ?? TryReadCreatedAtFromFileName(filePath)
            ?? GetFileCreatedAt(filePath);
        var title = TryReadTitle(preview)
            ?? Path.GetFileNameWithoutExtension(filePath);

        return new RecentNoteSummary(filePath, createdAt, title);
    }

    private static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static void DeleteDraftAssetsIfExists(string draftFilePath)
    {
        var draftAssetsDirectory = GetDraftAssetsDirectory(draftFilePath);
        if (Directory.Exists(draftAssetsDirectory))
        {
            TryDeleteDirectory(draftAssetsDirectory);
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetDraftAssetsDirectory(string draftFilePath)
    {
        var directory = Path.GetDirectoryName(draftFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Draft file path must include a directory.");
        }

        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(draftFilePath)}.assets");
    }

    private static void EnsureFileIsInsideNotesPath(string notesPath, string filePath)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(notesPath), filePath);
        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Recent note path must stay within the configured notes folder.");
        }
    }

    private static DateTimeOffset? TryReadCreatedAt(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var frontmatter = ExtractFrontmatter(normalized);
        if (frontmatter is null)
        {
            return null;
        }

        var match = CreatedFrontmatterRegex().Match(frontmatter);
        if (!match.Success)
        {
            return null;
        }

        return DateTimeOffset.TryParse(match.Groups["created"].Value, out var createdAt)
            ? createdAt
            : null;
    }

    private static string? ExtractFrontmatter(string normalizedContent)
    {
        if (!normalizedContent.StartsWith("---\n", StringComparison.Ordinal))
        {
            return null;
        }

        var endIndex = normalizedContent.IndexOf("\n---", 4, StringComparison.Ordinal);
        return endIndex < 0 ? null : normalizedContent[4..endIndex];
    }

    private static DateTimeOffset GetFileCreatedAt(string filePath)
    {
        return new DateTimeOffset(File.GetCreationTimeUtc(filePath), TimeSpan.Zero);
    }

    private static DateTimeOffset? TryReadCreatedAtFromFileName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        const int minuteTimestampLength = 15;
        const int secondTimestampLength = 17;
        if (fileName.Length < minuteTimestampLength)
        {
            return null;
        }

        if (fileName.Length >= secondTimestampLength
            && DateTimeOffset.TryParseExact(
                fileName[..secondTimestampLength],
                "yyyy-MM-dd-HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var secondResolutionCreatedAt))
        {
            return secondResolutionCreatedAt;
        }

        return DateTimeOffset.TryParseExact(
            fileName[..minuteTimestampLength],
            "yyyy-MM-dd-HHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var minuteResolutionCreatedAt)
            ? minuteResolutionCreatedAt
            : null;
    }

    private static string? TryReadTitle(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        using var reader = new StringReader(normalized);

        var inFrontmatter = normalized.StartsWith("---\n", StringComparison.Ordinal);
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (inFrontmatter)
            {
                if (lineNumber == 1)
                {
                    continue;
                }

                if (line == "---")
                {
                    inFrontmatter = false;
                }

                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal) && trimmed.Length > 2)
            {
                return trimmed[2..].Trim();
            }
        }

        return null;
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        return new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);
    }

    [GeneratedRegex(@"(?m)^created:\s*(?<created>\S+)\s*$")]
    private static partial Regex CreatedFrontmatterRegex();
}
