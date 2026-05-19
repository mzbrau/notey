using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Notey.App.Processing;
using Notey.Vault.Abstractions;
using Notey.Vault.Notes;

namespace Notey.App.Imports;

public sealed partial class ExistingDocumentImportService(
    INoteDraftStore draftStore,
    FileImportService fileImportService,
    DraftProcessingService draftProcessingService,
    IVaultWorkspace workspace,
    TimeProvider timeProvider,
    ILogger<ExistingDocumentImportService> logger)
{
    private const long MaxTextImportBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".markdown",
        ".txt"
    };

    public async Task<ExistingDocumentImportResult> ImportFolderAsync(
        string sourceFolderPath,
        IProgress<ExistingDocumentImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolderPath);

        var sourceRoot = Path.GetFullPath(sourceFolderPath);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Source folder '{sourceRoot}' does not exist.");
        }

        var sourceFiles = EnumerateSourceFiles(sourceRoot).ToArray();
        var referencedAssets = await FindReferencedAssetsAsync(sourceRoot, sourceFiles, cancellationToken);
        var items = new List<ExistingDocumentImportItemResult>();
        var importedImageReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sourceFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = sourceFiles[index];
            var relativePath = GetRelativePath(sourceRoot, sourceFile);
            if (referencedAssets.Contains(Path.GetFullPath(sourceFile)))
            {
                items.Add(ExistingDocumentImportItemResult.Skipped(sourceFile, "Imported as a referenced attachment."));
                progress?.Report(new ExistingDocumentImportProgress(index + 1, sourceFiles.Length, relativePath));
                continue;
            }

            try
            {
                var writtenPaths = await ImportSingleFileAsync(sourceRoot, sourceFile, importedImageReferences, cancellationToken);
                items.Add(ExistingDocumentImportItemResult.Imported(sourceFile, writtenPaths));
                logger.LogInformation("Imported source document {SourceFile} into Notey.", sourceFile);
            }
            catch (Exception ex) when (IsRecoverableImportFailure(ex))
            {
                items.Add(ExistingDocumentImportItemResult.Failed(sourceFile, ex.Message));
                logger.LogError(ex, "Failed to import source document {SourceFile}.", sourceFile);
            }

            progress?.Report(new ExistingDocumentImportProgress(index + 1, sourceFiles.Length, relativePath));
        }

        await RepairImportedDocumentLinksAsync(sourceRoot, items, cancellationToken);
        return new ExistingDocumentImportResult(sourceRoot, items);
    }

    private async Task<IReadOnlyList<string>> ImportSingleFileAsync(
        string sourceRoot,
        string sourceFile,
        IDictionary<string, string> importedImageReferences,
        CancellationToken cancellationToken)
    {
        var importWrittenPaths = new List<string>();
        NoteDraft? draft = null;
        try
        {
            draft = await draftStore.CreateAsync(timeProvider.GetLocalNow(), cancellationToken);
            var context = FileImportContext.ForDraft(draft.FilePath);
            var content = await BuildDraftContentAsync(sourceRoot, sourceFile, context, importWrittenPaths, importedImageReferences, cancellationToken);
            var sourceInfo = new FileInfo(sourceFile);
            var draftToProcess = draft with { Content = content };
            await draftStore.SaveAsync(draftToProcess, content, cancellationToken);
            var result = await draftProcessingService.ProcessAsync(
                draftToProcess,
                content,
                importContext: new DraftProcessingImportContext(
                    Path.GetFileName(sourceFile),
                    GetRelativePath(sourceRoot, sourceFile),
                    sourceInfo.Exists ? new DateTimeOffset(sourceInfo.LastWriteTimeUtc, TimeSpan.Zero) : null),
                cancellationToken: cancellationToken);

            return importWrittenPaths.Concat(result.WrittenPaths).ToArray();
        }
        catch
        {
            DraftAttachmentPromoter.DeleteFiles(importWrittenPaths);
            if (draft is not null)
            {
                DeleteIfExists(draft.FilePath);
                TryDeleteDirectory(AttachmentImportPaths.GetDraftAssetsDirectory(draft.FilePath));
            }

            throw;
        }
    }

    private async Task<string> BuildDraftContentAsync(
        string sourceRoot,
        string sourceFile,
        FileImportContext context,
        ICollection<string> importWrittenPaths,
        IDictionary<string, string> importedImageReferences,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourceFile);
        var relativePath = GetRelativePath(sourceRoot, sourceFile);
        if (IsTextImport(sourceFile))
        {
            var content = await File.ReadAllTextAsync(sourceFile, Encoding.UTF8, cancellationToken);
            content = await ImportReferencedAssetsAsync(sourceRoot, sourceFile, content, context, importWrittenPaths, importedImageReferences, cancellationToken);
            return $"""
                {content.Trim()}

                Imported from: {relativePath}
                """;
        }

        var importResult = await fileImportService.ImportAsync(
            [ImportFile.FromFilePath(sourceFile)],
            context,
            cancellationToken);
        foreach (var writtenPath in importResult.WrittenPaths)
        {
            importWrittenPaths.Add(writtenPath);
        }

        return string.Equals(extension, ".msg", StringComparison.OrdinalIgnoreCase)
            ? $"Source file: {relativePath}\n\n{importResult.Markdown.Trim()}"
            : $"Source file: {relativePath}\n\nImported attachment:\n{importResult.Markdown.Trim()}";
    }

    private async Task<string> ImportReferencedAssetsAsync(
        string sourceRoot,
        string sourceFile,
        string content,
        FileImportContext context,
        ICollection<string> importWrittenPaths,
        IDictionary<string, string> importedImageReferences,
        CancellationToken cancellationToken)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in ExtractReferences(content))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (replacements.ContainsKey(reference.Raw))
            {
                continue;
            }

            var assetPath = TryResolveSourceReference(sourceRoot, sourceFile, reference.Path);
            if (assetPath is null || !File.Exists(assetPath))
            {
                continue;
            }

            if (IsProcessableDocumentImport(assetPath))
            {
                continue;
            }

            var fullAssetPath = Path.GetFullPath(assetPath);
            if (AttachmentImportPaths.IsSupportedImageFileName(assetPath)
                && importedImageReferences.TryGetValue(fullAssetPath, out var cachedImageReference))
            {
                replacements[reference.Raw] = cachedImageReference;
                continue;
            }

            var importResult = await fileImportService.ImportAsync([ImportFile.FromFilePath(assetPath)], context, cancellationToken);
            foreach (var writtenPath in importResult.WrittenPaths)
            {
                importWrittenPaths.Add(writtenPath);
            }

            var markdown = importResult.Markdown.Trim();
            if (AttachmentImportPaths.IsSupportedImageFileName(assetPath))
            {
                importedImageReferences.TryAdd(fullAssetPath, markdown);
            }

            replacements[reference.Raw] = markdown;
        }

        foreach (var (raw, replacement) in replacements)
        {
            content = content.Replace(raw, replacement, StringComparison.Ordinal);
        }

        return content;
    }

    private async Task<HashSet<string>> FindReferencedAssetsAsync(
        string sourceRoot,
        IReadOnlyList<string> sourceFiles,
        CancellationToken cancellationToken)
    {
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFile in sourceFiles.Where(IsTextImport))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content;
            try
            {
                content = await File.ReadAllTextAsync(sourceFile, Encoding.UTF8, cancellationToken);
            }
            catch (Exception ex) when (IsRecoverableImportFailure(ex))
            {
                logger.LogWarning(ex, "Failed to scan references in {SourceFile}; it will be handled during import.", sourceFile);
                continue;
            }

            foreach (var reference in ExtractReferences(content))
            {
                var assetPath = TryResolveSourceReference(sourceRoot, sourceFile, reference.Path);
                if (assetPath is not null && File.Exists(assetPath) && !IsProcessableDocumentImport(assetPath))
                {
                    assets.Add(Path.GetFullPath(assetPath));
                }
            }
        }

        return assets;
    }

    private async Task RepairImportedDocumentLinksAsync(
        string sourceRoot,
        IReadOnlyList<ExistingDocumentImportItemResult> items,
        CancellationToken cancellationToken)
    {
        var importedDocuments = items
            .Where(static item => item.Status == ExistingDocumentImportItemStatus.Imported && IsProcessableDocumentImport(item.SourceFilePath))
            .Select(static item => new
            {
                SourcePath = Path.GetFullPath(item.SourceFilePath),
                FinalPath = TryGetFinalNotePath(item.WrittenPaths)
            })
            .Where(static item => item.FinalPath is not null)
            .ToDictionary(static item => item.SourcePath, static item => item.FinalPath!, StringComparer.OrdinalIgnoreCase);
        if (importedDocuments.Count == 0)
        {
            return;
        }

        foreach (var item in items.Where(static item => item.Status == ExistingDocumentImportItemStatus.Imported && IsTextImport(item.SourceFilePath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finalPath = TryGetFinalNotePath(item.WrittenPaths);
            if (finalPath is null)
            {
                continue;
            }

            string sourceContent;
            string finalContent;
            try
            {
                sourceContent = await File.ReadAllTextAsync(item.SourceFilePath, Encoding.UTF8, cancellationToken);
                finalContent = await File.ReadAllTextAsync(finalPath, Encoding.UTF8, cancellationToken);
            }
            catch (Exception ex) when (IsRecoverableImportFailure(ex))
            {
                logger.LogWarning(ex, "Failed to repair imported document links for {SourceFile}.", item.SourceFilePath);
                continue;
            }

            var updatedContent = finalContent;
            foreach (var reference in ExtractReferences(sourceContent))
            {
                var targetPath = TryResolveSourceReference(sourceRoot, item.SourceFilePath, reference.Path);
                if (targetPath is null || !importedDocuments.TryGetValue(Path.GetFullPath(targetPath), out var targetFinalPath))
                {
                    continue;
                }

                updatedContent = updatedContent.Replace(
                    reference.Raw,
                    BuildImportedDocumentLink(reference.Raw, targetFinalPath),
                    StringComparison.Ordinal);
            }

            if (!string.Equals(updatedContent, finalContent, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(finalPath, updatedContent, Utf8NoBom, cancellationToken);
            }
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles(string sourceRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        return Directory
            .EnumerateFiles(sourceRoot, "*", options)
            .Where(static filePath => !ShouldSkipPath(filePath))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldSkipPath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return filePath
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTextImport(string filePath)
    {
        return TextExtensions.Contains(Path.GetExtension(filePath)) && new FileInfo(filePath).Length <= MaxTextImportBytes;
    }

    private static bool IsProcessableDocumentImport(string filePath)
    {
        return IsTextImport(filePath) || string.Equals(Path.GetExtension(filePath), ".msg", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetFinalNotePath(IReadOnlyList<string> writtenPaths)
    {
        return writtenPaths.LastOrDefault(static path =>
            string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFileName(path), "tasks.md", StringComparison.OrdinalIgnoreCase)
            && !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(static segment => string.Equals(segment, "Draft", StringComparison.OrdinalIgnoreCase)));
    }

    private string BuildImportedDocumentLink(string rawReference, string targetFinalPath)
    {
        var relativePath = AttachmentImportPaths.GetVaultRelativePath(workspace.GetPaths(), targetFinalPath);
        var alias = Path.GetFileNameWithoutExtension(targetFinalPath);
        var prefix = rawReference.StartsWith('!') ? "!" : string.Empty;
        return $"{prefix}[[{relativePath}|{alias}]]";
    }

    private static string? TryResolveSourceReference(string sourceRoot, string sourceFile, string referencePath)
    {
        if (string.IsNullOrWhiteSpace(referencePath) || Uri.TryCreate(referencePath, UriKind.Absolute, out _))
        {
            return null;
        }

        var cleanReference = NormalizeReferenceTarget(referencePath);
        if (string.IsNullOrWhiteSpace(cleanReference))
        {
            return null;
        }

        cleanReference = Uri.UnescapeDataString(cleanReference).Replace('/', Path.DirectorySeparatorChar);
        var baseDirectory = Path.GetDirectoryName(sourceFile) ?? sourceRoot;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDirectory, cleanReference)),
            Path.GetFullPath(Path.Combine(sourceRoot, cleanReference))
        };

        return candidates.FirstOrDefault(candidate => IsUnderPath(sourceRoot, candidate));
    }

    private static string NormalizeReferenceTarget(string referencePath)
    {
        var trimmed = referencePath.Trim();
        if (trimmed.StartsWith('<'))
        {
            var closing = trimmed.IndexOf('>', StringComparison.Ordinal);
            trimmed = closing > 0 ? trimmed[1..closing] : trimmed.Trim('<', '>');
        }
        else if (trimmed.StartsWith('"') || trimmed.StartsWith('\''))
        {
            var quote = trimmed[0];
            var closing = trimmed.IndexOf(quote, 1);
            trimmed = closing > 1 ? trimmed[1..closing] : trimmed.Trim(quote);
        }
        else
        {
            var firstWhitespace = trimmed.IndexOfAny([' ', '\t']);
            if (firstWhitespace > 0)
            {
                trimmed = trimmed[..firstWhitespace];
            }
        }

        return trimmed.Trim().Split('#')[0];
    }

    private static IEnumerable<SourceReference> ExtractReferences(string content)
    {
        foreach (Match match in MarkdownImageRegex().Matches(content))
        {
            yield return new SourceReference(match.Value, match.Groups["path"].Value);
        }

        foreach (Match match in ObsidianImageEmbedRegex().Matches(content))
        {
            yield return new SourceReference(match.Value, match.Groups["path"].Value);
        }

        foreach (Match match in MarkdownLinkRegex().Matches(content))
        {
            yield return new SourceReference(match.Value, match.Groups["path"].Value);
        }

        foreach (Match match in ObsidianWikiLinkRegex().Matches(content))
        {
            yield return new SourceReference(match.Value, match.Groups["path"].Value);
        }
    }

    private static string GetRelativePath(string sourceRoot, string sourceFile)
    {
        return Path.GetRelativePath(sourceRoot, sourceFile)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static bool IsUnderPath(string directory, string filePath)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(filePath));
        return relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relativePath);
    }

    private static bool IsRecoverableImportFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or FormatException
            or NotSupportedException;
    }

    private static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record SourceReference(string Raw, string Path);

    [GeneratedRegex(@"!\[[^\]]*\]\((?<path>[^)]+)\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"!\[\[(?<path>[^\]|]+)(?:\|[^\]]+)?\]\]")]
    private static partial Regex ObsidianImageEmbedRegex();

    [GeneratedRegex(@"(?<!!)\[[^\]]+\]\((?<path>[^)]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?<!!)\[\[(?<path>[^\]|]+)(?:\|[^\]]+)?\]\]")]
    private static partial Regex ObsidianWikiLinkRegex();
}

public sealed record ExistingDocumentImportResult(
    string SourceRoot,
    IReadOnlyList<ExistingDocumentImportItemResult> Items)
{
    public int ImportedCount => Items.Count(static item => item.Status == ExistingDocumentImportItemStatus.Imported);

    public int FailedCount => Items.Count(static item => item.Status == ExistingDocumentImportItemStatus.Failed);

    public int SkippedCount => Items.Count(static item => item.Status == ExistingDocumentImportItemStatus.Skipped);
}

public sealed record ExistingDocumentImportProgress(int Completed, int Total, string CurrentRelativePath);

public sealed record ExistingDocumentImportItemResult(
    string SourceFilePath,
    ExistingDocumentImportItemStatus Status,
    IReadOnlyList<string> WrittenPaths,
    string? Message)
{
    public static ExistingDocumentImportItemResult Imported(string sourceFilePath, IReadOnlyList<string> writtenPaths)
    {
        return new ExistingDocumentImportItemResult(sourceFilePath, ExistingDocumentImportItemStatus.Imported, writtenPaths, null);
    }

    public static ExistingDocumentImportItemResult Failed(string sourceFilePath, string message)
    {
        return new ExistingDocumentImportItemResult(sourceFilePath, ExistingDocumentImportItemStatus.Failed, [], message);
    }

    public static ExistingDocumentImportItemResult Skipped(string sourceFilePath, string message)
    {
        return new ExistingDocumentImportItemResult(sourceFilePath, ExistingDocumentImportItemStatus.Skipped, [], message);
    }
}

public enum ExistingDocumentImportItemStatus
{
    Imported,
    Failed,
    Skipped
}
