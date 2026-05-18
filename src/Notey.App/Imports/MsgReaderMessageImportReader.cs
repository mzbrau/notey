using System.Text;
using MsgReader.Outlook;
using OutlookStorage = MsgReader.Outlook.Storage;

namespace Notey.App.Imports;

public sealed class MsgReaderMessageImportReader : IMessageImportReader
{
    private const int MaxEmbeddedMessageReadDepth = 3;
    private static readonly object LegacyEncodingsRegistrationGate = new();
    private static bool _legacyEncodingsRegistered;
    private readonly Func<Stream, OutlookStorage.Message> _messageFactory;

    public MsgReaderMessageImportReader()
        : this(static stream => new OutlookStorage.Message(stream, FileAccess.Read, leaveStreamOpen: false))
    {
    }

    internal MsgReaderMessageImportReader(Func<Stream, OutlookStorage.Message> messageFactory)
    {
        _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
    }

    public async ValueTask<ImportedEmailMessage> ReadAsync(ImportFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        EnsureLegacyEncodingsRegistered();
        await using var stream = await file.OpenReadAsync(cancellationToken);
        using var message = _messageFactory(stream);
        return ReadMessage(message, depth: 0);
    }

    internal static void EnsureLegacyEncodingsRegistered()
    {
        if (_legacyEncodingsRegistered)
        {
            return;
        }

        lock (LegacyEncodingsRegistrationGate)
        {
            if (_legacyEncodingsRegistered)
            {
                return;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _legacyEncodingsRegistered = true;
        }
    }

    private static ImportedEmailMessage ReadMessage(OutlookStorage.Message message, int depth)
    {
        var attachments = new List<ImportedEmailAttachment>();
        var embeddedMessages = new List<ImportedEmailMessage>();

        foreach (var item in message.Attachments ?? [])
        {
            switch (item)
            {
                case OutlookStorage.Attachment attachment when attachment.Data is { Length: > 0 } data:
                    attachments.Add(new ImportedEmailAttachment(
                        string.IsNullOrWhiteSpace(attachment.FileName) ? "attachment" : attachment.FileName,
                        data,
                        attachment.IsInline,
                        attachment.ContentId,
                        attachment.MimeType));
                    break;
                case OutlookStorage.Message embeddedMessage when depth < MaxEmbeddedMessageReadDepth:
                    embeddedMessages.Add(ReadMessage(embeddedMessage, depth + 1));
                    break;
            }
        }

        return new ImportedEmailMessage(
            message.Subject,
            FormatAddress(message.Sender?.DisplayName, message.Sender?.Email),
            message.GetEmailRecipients(RecipientType.To, true, true),
            message.GetEmailRecipients(RecipientType.Cc, true, true),
            message.SentOn,
            message.ReceivedOn,
            message.BodyText,
            message.BodyHtml,
            attachments,
            embeddedMessages);
    }

    private static string? FormatAddress(string? displayName, string? email)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return displayName.Trim();
        }

        return $"{displayName.Trim()} <{email.Trim()}>";
    }
}
