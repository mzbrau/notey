namespace Notey.App.Imports;

public sealed record ImportedEmailMessage(
    string? Subject,
    string? From,
    string? To,
    string? Cc,
    DateTimeOffset? SentOn,
    DateTimeOffset? ReceivedOn,
    string? BodyText,
    string? BodyHtml,
    IReadOnlyList<ImportedEmailAttachment> Attachments,
    IReadOnlyList<ImportedEmailMessage> EmbeddedMessages);

public sealed record ImportedEmailAttachment(
    string FileName,
    byte[] Data,
    bool IsInline,
    string? ContentId,
    string? MimeType);

public interface IMessageImportReader
{
    ValueTask<ImportedEmailMessage> ReadAsync(ImportFile file, CancellationToken cancellationToken = default);
}
