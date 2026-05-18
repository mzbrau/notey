using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Notey.Vault.Abstractions;

namespace Notey.App.Imports;

public sealed partial class DraftAttachmentPromoter(IVaultWorkspace workspace)
{
    public async Task<DraftAttachmentPromotionResult> PromoteAsync(
        string draftFilePath,
        string markdown,
        string finalNoteFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftFilePath);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalNoteFilePath);

        var draftAssetsDirectory = AttachmentImportPaths.GetDraftAssetsDirectory(draftFilePath);
        if (!Directory.Exists(draftAssetsDirectory))
        {
            return new DraftAttachmentPromotionResult(markdown, []);
        }

        var paths = workspace.GetPaths();
        var draftAssetsRelativePath = AttachmentImportPaths.GetVaultRelativePath(paths, draftAssetsDirectory).TrimEnd('/') + "/";
        var finalAssetsDirectory = AttachmentImportPaths.GetFinalAssetsDirectory(finalNoteFilePath);
        var promotedPaths = new List<string>();
        var linkMap = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var updatedMarkdown = WikiLinkRegex().Replace(markdown, match =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var linkPath = match.Groups["path"].Value.Trim();
                if (!IsDraftAssetLink(linkPath, draftAssetsRelativePath))
                {
                    return match.Value;
                }

                if (!linkMap.TryGetValue(linkPath, out var finalRelativePath))
                {
                    var stagedPath = AttachmentImportPaths.TryResolveVaultRelativePath(paths, linkPath)
                        ?? throw new InvalidOperationException("Draft attachment link could not be resolved inside the vault.");
                    if (!IsPathUnderDirectory(stagedPath, draftAssetsDirectory))
                    {
                        throw new InvalidOperationException("Draft attachment link must resolve inside the draft assets folder.");
                    }

                    if (!File.Exists(stagedPath))
                    {
                        throw new FileNotFoundException("Draft attachment link points to a missing staged file.", stagedPath);
                    }

                    Directory.CreateDirectory(finalAssetsDirectory);
                    var finalPath = AttachmentImportPaths.GetUniqueFilePath(finalAssetsDirectory, Path.GetFileName(stagedPath));
                    File.Copy(stagedPath, finalPath);
                    promotedPaths.Add(finalPath);
                    finalRelativePath = AttachmentImportPaths.GetVaultRelativePath(paths, finalPath);
                    linkMap.Add(linkPath, finalRelativePath);
                }

                return $"{match.Groups["prefix"].Value}{finalRelativePath}{match.Groups["alias"].Value}]]";
            });

            await Task.CompletedTask;
            return new DraftAttachmentPromotionResult(updatedMarkdown, promotedPaths);
        }
        catch
        {
            DeleteFiles(promotedPaths);
            throw;
        }
    }

    public void DeleteDraftAssetsDirectory(string draftFilePath, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftFilePath);
        ArgumentNullException.ThrowIfNull(logger);

        var draftAssetsDirectory = AttachmentImportPaths.GetDraftAssetsDirectory(draftFilePath);
        if (!Directory.Exists(draftAssetsDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(draftAssetsDirectory, recursive: true);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to delete draft attachment staging folder {DraftAssetsDirectory}.", draftAssetsDirectory);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Notey does not have permission to delete draft attachment staging folder {DraftAssetsDirectory}.", draftAssetsDirectory);
        }
    }

    public static void DeleteFiles(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsDraftAssetLink(string linkPath, string draftAssetsRelativePath)
    {
        var normalized = linkPath.Replace('\\', '/');
        return normalized.StartsWith(draftAssetsRelativePath, StringComparison.Ordinal);
    }

    private static bool IsPathUnderDirectory(string filePath, string directoryPath)
    {
        var normalizedFilePath = Path.GetFullPath(filePath);
        var normalizedDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relativePath = Path.GetRelativePath(normalizedDirectoryPath, normalizedFilePath);
        return relativePath.Length > 0
            && relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relativePath);
    }

    [GeneratedRegex(@"(?<prefix>!?\[\[)(?<path>[^\]|]+)(?<alias>\|[^\]]+)?\]\]")]
    private static partial Regex WikiLinkRegex();
}

public sealed record DraftAttachmentPromotionResult(string Markdown, IReadOnlyList<string> PromotedPaths);
