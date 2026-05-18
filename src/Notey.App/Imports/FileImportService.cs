using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Notey.Vault.Abstractions;
using Notey.Vault.Linking;

namespace Notey.App.Imports;

public sealed partial class FileImportService(
    IVaultWorkspace workspace,
    ObsidianLinkBuilder linkBuilder,
    IMessageImportReader messageReader)
{
    private const int MaxMessageImportDepth = 3;
    private const int MaxMessageAttachmentCount = 50;

    public async Task<FileImportResult> ImportAsync(
        IReadOnlyList<ImportFile> files,
        FileImportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.IsDraft && !context.IsFinalNote)
        {
            throw new InvalidOperationException("Files can only be imported into an active draft or final note.");
        }

        var markdownBlocks = new List<string>();
        var writtenPaths = new List<string>();
        var attachmentBudget = new ImportBudget(MaxMessageAttachmentCount);

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ImportSingleAsync(file, context, depth: 0, attachmentBudget, cancellationToken);
                if (!string.IsNullOrWhiteSpace(result.Markdown))
                {
                    markdownBlocks.Add(result.Markdown.Trim());
                }

                writtenPaths.AddRange(result.WrittenPaths);
            }

            return new FileImportResult(string.Join("\n\n", markdownBlocks), writtenPaths);
        }
        catch
        {
            DraftAttachmentPromoter.DeleteFiles(writtenPaths);
            throw;
        }
    }

    private async Task<FileImportResult> ImportSingleAsync(
        ImportFile file,
        FileImportContext context,
        int depth,
        ImportBudget attachmentBudget,
        CancellationToken cancellationToken)
    {
        if (depth > MaxMessageImportDepth)
        {
            return new FileImportResult($"- Attachment `{file.FileName}` skipped because nested email import depth exceeded {MaxMessageImportDepth}.", []);
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.Equals(extension, ".msg", StringComparison.OrdinalIgnoreCase))
        {
            return await ImportMessageAsync(file, context, depth, attachmentBudget, cancellationToken);
        }

        return AttachmentImportPaths.IsSupportedImageFileName(file.FileName)
            ? await ImportImageAsync(file, cancellationToken)
            : await ImportAttachmentAsync(file, context, cancellationToken);
    }

    private async Task<FileImportResult> ImportImageAsync(ImportFile file, CancellationToken cancellationToken)
    {
        var paths = workspace.GetPaths();
        Directory.CreateDirectory(paths.ImagesPath);

        var targetPath = AttachmentImportPaths.GetUniqueFilePath(paths.ImagesPath, file.FileName);
        await CopyToFileAsync(file, targetPath, cancellationToken);

        return new FileImportResult(linkBuilder.BuildImageEmbed(targetPath), [targetPath]);
    }

    private async Task<FileImportResult> ImportAttachmentAsync(
        ImportFile file,
        FileImportContext context,
        CancellationToken cancellationToken)
    {
        var paths = workspace.GetPaths();
        var targetDirectory = context.IsFinalNote
            ? AttachmentImportPaths.GetFinalAssetsDirectory(context.FinalNotePath!)
            : AttachmentImportPaths.GetDraftAssetsDirectory(context.DraftFilePath!);
        Directory.CreateDirectory(targetDirectory);

        var targetPath = AttachmentImportPaths.GetUniqueFilePath(targetDirectory, file.FileName);
        await CopyToFileAsync(file, targetPath, cancellationToken);
        var link = AttachmentImportPaths.BuildAttachmentLink(paths, targetPath, Path.GetFileName(targetPath));

        return new FileImportResult(link, [targetPath]);
    }

    private async Task<FileImportResult> ImportMessageAsync(
        ImportFile file,
        FileImportContext context,
        int depth,
        ImportBudget attachmentBudget,
        CancellationToken cancellationToken)
    {
        var message = await messageReader.ReadAsync(file, cancellationToken);
        return await ImportMessageAsync(message, context, depth, attachmentBudget, cancellationToken);
    }

    private async Task<FileImportResult> ImportMessageAsync(
        ImportedEmailMessage message,
        FileImportContext context,
        int depth,
        ImportBudget attachmentBudget,
        CancellationToken cancellationToken)
    {
        if (depth > MaxMessageImportDepth)
        {
            return new FileImportResult("Email import skipped because the recursion limit was reached.", []);
        }

        var markdown = new StringBuilder();
        AppendMessageMarkdown(markdown, message, headingLevel: Math.Min(2 + depth, 6));
        var writtenPaths = new List<string>();
        var attachmentMarkdown = new List<string>();

        foreach (var attachment in message.Attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attachment.IsInline && !string.IsNullOrWhiteSpace(attachment.ContentId))
            {
                continue;
            }

            if (!attachmentBudget.TryTake())
            {
                attachmentMarkdown.Add("- Further attachments skipped because the email import limit was reached.");
                break;
            }

            var result = await ImportSingleAsync(
                ImportFile.FromBytes(attachment.FileName, attachment.Data),
                context,
                depth + 1,
                attachmentBudget,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Markdown))
            {
                attachmentMarkdown.Add(AttachmentImportPaths.ToReferenceText(result.Markdown));
            }

            writtenPaths.AddRange(result.WrittenPaths);
        }

        foreach (var embeddedMessage in message.EmbeddedMessages)
        {
            if (!attachmentBudget.TryTake())
            {
                attachmentMarkdown.Add("- Further embedded messages skipped because the email import limit was reached.");
                break;
            }

            var embeddedMarkdown = new StringBuilder();
            var embeddedResult = await ImportMessageAsync(embeddedMessage, context, depth + 1, attachmentBudget, cancellationToken);
            embeddedMarkdown.Append(embeddedResult.Markdown);
            attachmentMarkdown.Add(embeddedMarkdown.ToString().Trim());
            writtenPaths.AddRange(embeddedResult.WrittenPaths);
        }

        if (attachmentMarkdown.Count > 0)
        {
            markdown.AppendLine();
            markdown.AppendLine($"{Heading(Math.Min(3 + depth, 6))} Attachments");
            foreach (var item in attachmentMarkdown)
            {
                markdown.AppendLine(item);
            }
        }

        return new FileImportResult(markdown.ToString().Trim(), writtenPaths);
    }

    private static async Task CopyToFileAsync(ImportFile file, string targetPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = await file.OpenReadAsync(cancellationToken);
            await using var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 81920, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        catch
        {
            DeletePartialCopy(targetPath);
            throw;
        }
    }

    private static void DeletePartialCopy(string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AppendMessageMarkdown(StringBuilder builder, ImportedEmailMessage message, int headingLevel)
    {
        var subject = string.IsNullOrWhiteSpace(message.Subject) ? "Email import" : message.Subject.Trim();
        builder.AppendLine($"{Heading(headingLevel)} Email: {subject}");
        builder.AppendLine();
        builder.AppendLine("| Field | Value |");
        builder.AppendLine("| --- | --- |");
        AppendMetadataRow(builder, "From", message.From);
        AppendMetadataRow(builder, "To", message.To);
        AppendMetadataRow(builder, "Cc", message.Cc);
        AppendMetadataRow(builder, "Subject", message.Subject);
        AppendMetadataRow(builder, "Sent", FormatTimestamp(message.SentOn));
        AppendMetadataRow(builder, "Received", FormatTimestamp(message.ReceivedOn));
        builder.AppendLine();

        var body = NormalizeBody(message.BodyText);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = HtmlToPlainText(message.BodyHtml);
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.AppendLine(body.Trim());
            builder.AppendLine();
        }
    }

    private static void AppendMetadataRow(StringBuilder builder, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"| {field} | {EscapeTableCell(value)} |");
    }

    private static string EscapeTableCell(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal)
            .Replace("\r", "<br>", StringComparison.Ordinal)
            .Trim();
    }

    private static string? FormatTimestamp(DateTimeOffset? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
    }

    private static string NormalizeBody(string? body)
    {
        return string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
    }

    private static string HtmlToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var normalized = html
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        normalized = BreakRegex().Replace(normalized, "\n");
        normalized = ParagraphRegex().Replace(normalized, "\n\n");
        normalized = TagsRegex().Replace(normalized, string.Empty);
        return WebUtility.HtmlDecode(normalized).Trim();
    }

    private static string Heading(int level)
    {
        return new string('#', Math.Clamp(level, 1, 6));
    }

    [GeneratedRegex(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"</\s*p\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagsRegex();

    private sealed class ImportBudget(int remaining)
    {
        public bool TryTake()
        {
            if (remaining <= 0)
            {
                return false;
            }

            remaining--;
            return true;
        }
    }
}
