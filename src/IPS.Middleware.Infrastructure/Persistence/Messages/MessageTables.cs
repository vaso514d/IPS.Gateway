using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.History;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.Messages;

// Keeps one row per payment in its message table (specification 020), in the transaction that changes the payment: the
// registering save adds the row from the request, and every later save brings its current status up to date. Payments
// registered before a table existed have no row and are left alone (no backfill, ADR 0003).
internal static class MessageTables
{
    internal static async Task ApplyAsync(
        TransactionDbContext db,
        IEnumerable<(AggregateRoot Aggregate, IReadOnlyList<DomainEvent> Events)> changes,
        CancellationToken cancellationToken)
    {
        foreach (var (aggregate, events) in changes)
        {
            var registers = events.Any(occurrence => occurrence is PaymentReceived or IncomingPaymentRegistered or IncomingTransferRegistered);
            var row = registers ? Create(db, aggregate) : await FindAsync(db, aggregate, cancellationToken);
            if (row is null)
            {
                continue;
            }

            Stamp(row, aggregate, events);
            if (registers)
            {
                db.Add(row);
            }
        }
    }

    // The table of each payment: outgoing by message type, an incoming pacs.008 payment, incoming transfers by kind.
    private static Type? RowType(AggregateRoot aggregate) => aggregate switch
    {
        OutgoingPayment payment => payment.MessageType switch
        {
            PaymentMessageTypes.Pacs008 => typeof(Pacs008PaymentRow),
            PaymentMessageTypes.Pacs009 => typeof(Pacs009PaymentRow),
            PaymentMessageTypes.Pacs004 => typeof(Pacs004PaymentRow),
            _ => null
        },
        IncomingPayment => typeof(Pacs008PaymentRow),
        IncomingTransfer transfer => transfer.Kind switch
        {
            PaymentMessageTypes.Pacs009 => typeof(Pacs009PaymentRow),
            PaymentMessageTypes.Pacs004 => typeof(Pacs004PaymentRow),
            _ => null
        },
        _ => null
    };

    private static MessageRow? Create(TransactionDbContext db, AggregateRoot aggregate) => (aggregate, RowType(aggregate)) switch
    {
        (_, null) => null,
        (OutgoingPayment payment, _) => OutgoingRows.Create(payment, db.Metadata(payment)),
        (IncomingPayment payment, _) => Pacs008Rows.Incoming(payment, db.Metadata(payment)),
        (IncomingTransfer transfer, _) => IncomingRows.Create(transfer,
            IncomingTransferJson.Read(transfer.Kind, db.IncomingTransferMetadata.Local.Single(metadata => metadata.Id == transfer.Id).RequestJson)),
        _ => null
    };

    private static async Task<MessageRow?> FindAsync(TransactionDbContext db, AggregateRoot aggregate, CancellationToken cancellationToken) =>
        RowType(aggregate) is { } type ? (MessageRow?)await db.FindAsync(type, [aggregate.Id], cancellationToken) : null;

    // The current status, as the aggregate holds it; an incoming one's is derived as in its status history.
    private static void Stamp(MessageRow row, AggregateRoot aggregate, IReadOnlyList<DomainEvent> events)
    {
        switch (aggregate)
        {
            case OutgoingPayment payment:
                row.Status = payment.CurrentStatus;
                row.StatusAtUtc = payment.CurrentStatusAtUtc;
                row.ReasonCode = Fit(payment.CurrentReasonCode, MessageRow.ReferenceLength);
                row.Description = Fit(payment.CurrentDescription, MessageRow.DescriptionLength);
                break;
            case IncomingPayment payment:
                row.Status = TransferStatusHistory.IncomingStatus(payment.CoreStatus, payment.IpsDecision, payment.FollowUp);
                row.StatusAtUtc = events.Max(occurrence => occurrence.OccurredAtUtc);
                row.ReasonCode = Fit(payment.IpsReasonCode ?? payment.CoreReasonCode, MessageRow.ReferenceLength);
                row.Description = Fit(payment.ManualReviewReason ?? payment.IpsDescription ?? payment.CoreDescription, MessageRow.DescriptionLength);
                row.CoreReference = Fit(payment.CoreReference, 256);
                break;
            case IncomingTransfer transfer:
                row.Status = transfer.ManualReviewReason is null ? TransferStatusHistory.TransferStatus(transfer.CoreStatus) : TransactionStatus.ManualReview;
                row.StatusAtUtc = events.Max(occurrence => occurrence.OccurredAtUtc);
                row.ReasonCode = Fit(transfer.CoreReasonCode, MessageRow.ReferenceLength);
                row.Description = Fit(transfer.ManualReviewReason ?? transfer.CoreDescription, MessageRow.DescriptionLength);
                row.CoreReference = Fit(transfer.CoreReference, 256);
                break;
        }
    }

    // A sent message's row, from its accepted snapshot; the snapshot is absent only for a request that failed validation.
    private static class OutgoingRows
    {
        internal static MessageRow? Create(OutgoingPayment payment, OutgoingPaymentMetadata metadata)
        {
            var accepted = PaymentJson.ReadAccepted(payment.MessageType, metadata.AcceptedJson);
            MessageRow? row = payment.MessageType switch
            {
                PaymentMessageTypes.Pacs008 => Pacs008Rows.Outgoing((accepted as AcceptedPacs008)?.Payment),
                PaymentMessageTypes.Pacs009 => Pacs009Rows.Outgoing((accepted as AcceptedPacs009)?.Payment),
                PaymentMessageTypes.Pacs004 => Pacs004Rows.Outgoing((accepted as AcceptedPacs004)?.Payment),
                _ => null
            };
            if (row is null)
            {
                return null;
            }

            row.Id = payment.Id;
            row.Direction = TransactionDirection.Outgoing;
            row.ClientReference = payment.ClientReference;
            row.MessageId = metadata.MessageId;
            row.CreatedAtUtc = payment.CreatedAtUtc;
            switch (row)
            {
                case Pacs008PaymentRow pacs008:
                    pacs008.TransactionId = metadata.ProtocolTransactionId;
                    break;
                case Pacs009PaymentRow pacs009:
                    pacs009.TransactionId ??= metadata.ProtocolTransactionId;
                    break;
            }

            return row;
        }
    }

    // A received transfer's row, from the content stored at registration.
    private static class IncomingRows
    {
        internal static MessageRow? Create(IncomingTransfer transfer, IIncomingTransferContent content)
        {
            MessageRow? row = content switch
            {
                IncomingPacs009 pacs009 => Pacs009Rows.Incoming(pacs009),
                IncomingPacs004 pacs004 => Pacs004Rows.Incoming(pacs004),
                _ => null
            };
            if (row is null)
            {
                return null;
            }

            row.Id = transfer.Id;
            row.Direction = TransactionDirection.Incoming;
            row.CreatedAtUtc = transfer.RegisteredAtUtc;
            return row;
        }
    }

    // External text is cut to its column rather than failing the payment's save.
    internal static string? Fit(string? value, int length) => value is { Length: var size } && size > length ? value[..length] : value;

    private static class Pacs008Rows
    {
        private static readonly TimeSpan SettlementOffset = TimeSpan.FromHours(4);

        internal static Pacs008PaymentRow Outgoing(OutgoingPayment payment, OutgoingPaymentMetadata metadata)
        {
            var sent = (PaymentJson.ReadAccepted(payment.MessageType, metadata.AcceptedJson) as AcceptedPacs008)?.Payment;
            return new Pacs008PaymentRow
            {
                Id = payment.Id,
                Direction = TransactionDirection.Outgoing,
                ClientReference = payment.ClientReference,
                MessageId = metadata.MessageId,
                CreatedAtUtc = payment.CreatedAtUtc,
                TransactionId = metadata.ProtocolTransactionId,
                InstructionId = sent?.InstructionId,
                EndToEndId = sent?.EndToEndId,
                Amount = sent?.Amount,
                Currency = sent?.Currency,
                ValueDate = sent is null ? null : DateOnly.FromDateTime(sent.AcceptanceDateTime.ToOffset(SettlementOffset).DateTime),
                AcceptanceDateTime = sent?.AcceptanceDateTime,
                DebtorName = Fit(sent?.Debtor.Name, 140),
                DebtorAccount = sent?.DebtorAccount.Value,
                DebtorIdentification = Fit(sent?.Debtor.Identifier, 256),
                DebtorAgentBic = sent?.DebtorAgent.Bic,
                CreditorName = Fit(sent?.Creditor.Name, 140),
                CreditorAccount = sent?.CreditorAccount.Value,
                CreditorIdentification = Fit(sent?.Creditor.Identifier, 256),
                CreditorAgentBic = sent?.CreditorAgent.Bic,
                CategoryPurpose = sent?.CategoryPurposeCode,
                RemittanceInformation = Fit(sent?.Remittance?.Unstructured, MessageRow.DescriptionLength)
            };
        }

        internal static Pacs008PaymentRow Incoming(IncomingPayment payment, IncomingPaymentMetadata metadata)
        {
            var received = IncomingPaymentJson.Read<Pacs008Request>(metadata.RequestJson);
            var original = IncomingPaymentJson.Read<IncomingProcessingContext>(metadata.ContextJson).Original;
            return new Pacs008PaymentRow
            {
                Id = payment.Id,
                Direction = TransactionDirection.Incoming,
                MessageId = original.GroupMessageId,
                CreatedAtUtc = payment.RegisteredAtUtc,
                TransactionId = original.TransactionId,
                Uetr = original.Uetr,
                InstructionId = received.InstructionId,
                EndToEndId = payment.EndToEndId,
                Amount = received.Amount,
                Currency = received.Currency,
                ValueDate = original.SettlementDate,
                AcceptanceDateTime = received.AcceptanceDateTime,
                DebtorName = Fit(received.Debtor?.Name, 140),
                DebtorAccount = Fit(received.Debtor?.Account, 34),
                DebtorIdentification = Fit(received.Debtor?.Identifier, 256),
                DebtorAgentBic = Fit(original.DebtorAgentBic ?? received.Debtor?.ParticipantBic, 11),
                CreditorName = Fit(received.Creditor?.Name, 140),
                CreditorAccount = Fit(received.Creditor?.Account, 34),
                CreditorIdentification = Fit(received.Creditor?.Identifier, 256),
                CreditorAgentBic = Fit(received.Creditor?.ParticipantBic, 11),
                CategoryPurpose = Fit(received.CategoryPurposeCode, MessageRow.ReferenceLength),
                RemittanceInformation = Fit(received.Remittance?.Unstructured, MessageRow.DescriptionLength)
            };
        }
    }
}
