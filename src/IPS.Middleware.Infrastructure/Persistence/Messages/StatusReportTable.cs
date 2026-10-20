using System.Xml;
using System.Xml.Linq;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Persistence.Messages;

// pacs.002 rows (specification 020): outgoing, the gateway's reply to a received pacs.008, kept in IncomingReplies; incoming,
// a status report IPS sends on its own, kept in the inbound journal. Neither is a payment aggregate, so the changes are
// captured before the first save, while the tracker still knows what is new, and written in the same transaction.
internal static class StatusReportTable
{
    private static readonly XNamespace Pacs002 = Pacs008Schema.ReplyNamespace;

    internal static IReadOnlyList<Change> Capture(TransactionDbContext db)
    {
        var replies = db.ChangeTracker.Entries<IncomingReplyRow>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => new Change(Reply(entry.Entity), entry.State == EntityState.Added));
        var reports = db.ChangeTracker.Entries<InboundJournalEntry>()
            .Where(entry => entry.State == EntityState.Added && PaymentMessageTypes.IsPacs002(entry.Entity.MessageType))
            .Select(entry => new Change(Report(entry.Entity), IsNew: true));
        return replies.Concat(reports).ToArray();
    }

    internal static async Task ApplyAsync(TransactionDbContext db, IReadOnlyList<Change> changes, CancellationToken cancellationToken)
    {
        foreach (var change in changes)
        {
            if (change.IsNew)
            {
                db.Add(change.Row);
                continue;
            }

            if (await db.Set<Pacs002StatusReportRow>().FindAsync([change.Row.Id], cancellationToken) is { } existing)
            {
                existing.Status = change.Row.Status;
                existing.StatusAtUtc = change.Row.StatusAtUtc;
                existing.ReasonCode = change.Row.ReasonCode;
                existing.Description = change.Row.Description;
            }
        }
    }

    // The reply's status is its delivery to IPS: being prepared or sent, delivered, or held for an operator.
    private static Pacs002StatusReportRow Reply(IncomingReplyRow reply)
    {
        var envelope = IncomingPaymentJson.Read<IncomingReplyEnvelope>(reply.EnvelopeJson);
        var created = envelope.Context.CreatedAtUtc;
        return new Pacs002StatusReportRow
        {
            Id = reply.JournalId,
            Direction = TransactionDirection.Outgoing,
            Status = reply.Status switch
            {
                IncomingReplyStatus.Delivered => TransactionStatus.Accepted,
                IncomingReplyStatus.ManualReview => TransactionStatus.ManualReview,
                _ => TransactionStatus.Sending
            },
            StatusAtUtc = reply.Status is IncomingReplyStatus.Preparing ? created : DateTimeOffset.UtcNow,
            Description = MessageTables.Fit(reply.ReviewReason, MessageRow.DescriptionLength),
            MessageId = MessageTables.Fit(envelope.Context.MessageId, MessageRow.ReferenceLength),
            CreatedAtUtc = created,
            OriginalMessageId = MessageTables.Fit(envelope.Original.GroupMessageId, MessageRow.ReferenceLength),
            OriginalEndToEndId = MessageTables.Fit(envelope.Original.EndToEndId, MessageRow.ReferenceLength),
            OriginalTransactionId = MessageTables.Fit(envelope.Original.TransactionId, MessageRow.ReferenceLength),
            TransactionStatusCode = envelope.Decision.Accepted ? "ACCP" : "RJCT",
            ReportReasonCode = MessageTables.Fit(envelope.Decision.ReasonCode, MessageRow.ReferenceLength),
            AdditionalInformation = MessageTables.Fit(envelope.Decision.Description, 256)
        };
    }

    // An unsolicited report is recorded as received; content that cannot be read leaves its fields empty.
    private static Pacs002StatusReportRow Report(InboundJournalEntry entry)
    {
        var row = new Pacs002StatusReportRow
        {
            Id = entry.Id,
            Direction = TransactionDirection.Incoming,
            Status = TransactionStatus.Received,
            StatusAtUtc = entry.ReceivedAtUtc,
            CreatedAtUtc = entry.ReceivedAtUtc
        };
        if (Business(entry.RawXml) is not { } report)
        {
            return row;
        }

        var group = report.Element(Pacs002 + "OrgnlGrpInfAndSts");
        var transaction = report.Element(Pacs002 + "TxInfAndSts");
        row.MessageId = Value(report.Element(Pacs002 + "GrpHdr"), "MsgId");
        row.OriginalMessageId = Value(group, "OrgnlMsgId");
        row.OriginalEndToEndId = Value(transaction, "OrgnlEndToEndId");
        row.OriginalTransactionId = Value(transaction, "OrgnlTxId");
        row.TransactionStatusCode = Value(transaction, "TxSts") ?? Value(group, "GrpSts");
        row.ReportReasonCode = MessageTables.Fit(report.Descendants(Pacs002 + "Rsn").Elements().FirstOrDefault()?.Value.Trim(), MessageRow.ReferenceLength);
        row.AdditionalInformation = MessageTables.Fit(report.Descendants(Pacs002 + "AddtlInf").FirstOrDefault()?.Value.Trim(), 256);
        return row;
    }

    private static XElement? Business(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
            return IpsEnvelope.Open(XDocument.Load(reader).Root, Pacs002)?.Document.Elements().SingleOrDefault();
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string? Value(XElement? parent, string name) =>
        MessageTables.Fit(parent?.Element(Pacs002 + name)?.Value.Trim(), MessageRow.ReferenceLength);

    internal sealed record Change(Pacs002StatusReportRow Row, bool IsNew);
}
