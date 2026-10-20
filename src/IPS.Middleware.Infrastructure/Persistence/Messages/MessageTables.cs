using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.Application.Payments.Pain002;
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
            PaymentMessageTypes.Camt056 => typeof(Camt056RecallRow),
            PaymentMessageTypes.Camt029 => typeof(Camt029ResolutionRow),
            PaymentMessageTypes.Pain002 => typeof(Pain002StatusReportRow),
            _ => null
        },
        IncomingPayment => typeof(Pacs008PaymentRow),
        IncomingTransfer transfer => transfer.Kind switch
        {
            PaymentMessageTypes.Pacs009 => typeof(Pacs009PaymentRow),
            PaymentMessageTypes.Pacs004 => typeof(Pacs004PaymentRow),
            PaymentMessageTypes.Pain001 => typeof(Pain001InitiationRow),
            PaymentMessageTypes.Camt056 => typeof(Camt056RecallRow),
            PaymentMessageTypes.Camt055 => typeof(Camt055CancellationRow),
            PaymentMessageTypes.Camt029 => typeof(Camt029ResolutionRow),
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
                PaymentMessageTypes.Camt056 => RecallRows.Outgoing((accepted as AcceptedCamt056)?.Payment),
                PaymentMessageTypes.Camt029 => RecallRows.Outgoing((accepted as AcceptedCamt029)?.Payment),
                PaymentMessageTypes.Pain002 => RecallRows.Outgoing((accepted as AcceptedPain002)?.Payment),
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
                IncomingPain001 pain001 => RecallRows.Incoming(pain001),
                IncomingCamt056 camt056 => RecallRows.Incoming(camt056),
                IncomingCamt055 camt055 => RecallRows.Incoming(camt055),
                IncomingCamt029 camt029 => RecallRows.Incoming(camt029),
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

        internal static Pacs008PaymentRow Outgoing(ValidatedPacs008? sent) =>
            new()
            {
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

    private static class Pacs009Rows
    {
        internal static Pacs009PaymentRow Outgoing(ValidatedPacs009? sent) =>
            new()
            {
                InstructionId = sent?.InstructionId,
                EndToEndId = sent?.EndToEndId,
                TransactionId = sent?.TransactionId,
                Amount = sent?.Amount,
                Currency = sent?.Currency,
                ValueDate = sent?.ValueDate,
                DebtorAgentBic = sent?.DebtorAgentBic,
                DebtorAccount = sent?.DebtorAccount,
                CreditorAgentBic = sent?.CreditorAgentBic,
                CreditorAccount = sent?.CreditorAccount,
                CategoryPurpose = Fit(sent?.CategoryPurpose, MessageRow.ReferenceLength),
                Purpose = Fit(sent?.Purpose, MessageRow.ReferenceLength)
            };

        internal static Pacs009PaymentRow Incoming(IncomingPacs009 received) =>
            new()
            {
                MessageId = received.MessageId,
                InstructionId = received.InstructionId,
                EndToEndId = received.EndToEndId,
                TransactionId = received.TransactionId,
                Uetr = received.Uetr,
                Amount = received.Amount,
                Currency = received.Currency,
                ValueDate = received.ValueDate,
                DebtorAgentBic = Fit(received.DebtorAgent.Bic, 11),
                DebtorAccount = Fit(received.DebtorAccount, 34),
                CreditorAgentBic = Fit(received.CreditorAgent.Bic, 11),
                CreditorAccount = Fit(received.CreditorAccount, 34),
                CategoryPurpose = Fit(received.CategoryPurpose?.Value, MessageRow.ReferenceLength),
                Purpose = Fit(received.Purpose, MessageRow.ReferenceLength)
            };
    }

    private static class Pacs004Rows
    {
        internal static Pacs004PaymentRow Outgoing(ValidatedPacs004? sent) =>
            new()
            {
                ReturnId = sent?.ReturnId,
                ReturnReasonCode = sent?.ReturnReasonCode,
                Amount = sent?.Amount,
                Currency = sent?.Currency,
                ValueDate = sent?.ValueDate,
                InstructedAgentBic = sent?.InstructedAgentBic,
                DebtorName = Fit(sent?.Debtor.Name, 140),
                DebtorAccount = sent?.Debtor.Account,
                CreditorName = Fit(sent?.Creditor.Name, 140),
                CreditorAccount = sent?.Creditor.Account,
                OriginalMessageId = sent?.Original.MessageId,
                OriginalMessageNameId = sent?.Original.MessageNameId,
                OriginalEndToEndId = sent?.Original.EndToEndId,
                OriginalTransactionId = sent?.Original.TransactionId
            };

        internal static Pacs004PaymentRow Incoming(IncomingPacs004 received) =>
            new()
            {
                ReturnId = received.ReturnId,
                ReturnReasonCode = Fit(received.ReturnReasonCode, MessageRow.ReferenceLength),
                Amount = received.Amount,
                Currency = received.Currency,
                ValueDate = received.ValueDate,
                InstructedAgentBic = Fit(received.InstructedAgent, 11),
                DebtorName = Fit(received.Debtor?.Name, 140),
                DebtorAccount = Fit(received.Debtor?.Account, 34),
                CreditorName = Fit(received.Creditor?.Name, 140),
                CreditorAccount = Fit(received.Creditor?.Account, 34),
                OriginalMessageId = Fit(received.Original.MessageId, MessageRow.ReferenceLength),
                OriginalMessageNameId = Fit(received.Original.MessageNameId, MessageRow.ReferenceLength),
                OriginalInstructionId = Fit(received.Original.InstructionId, MessageRow.ReferenceLength),
                OriginalEndToEndId = Fit(received.Original.EndToEndId, MessageRow.ReferenceLength),
                OriginalTransactionId = Fit(received.Original.TransactionId, MessageRow.ReferenceLength),
                OriginalUetr = received.Original.Uetr,
                AdditionalInformation = Fit(received.AdditionalInformation, 256)
            };
    }

    // Initiations, recalls, cancellations, resolutions and pain.002 status reports.
    private static class RecallRows
    {
        internal static Camt056RecallRow Outgoing(ValidatedCamt056? sent) =>
            new()
            {
                RecallId = sent?.RecallId,
                OriginalMessageId = sent?.OriginalMessageId,
                OriginalEndToEndId = sent?.OriginalEndToEndId,
                OriginalTransactionId = sent?.OriginalTransactionId,
                OriginalAmount = sent?.OriginalAmount,
                OriginalCurrency = sent?.OriginalCurrency,
                OriginalSettlementDate = sent?.OriginalSettlementDate,
                RecallReasonCode = sent?.ReasonCode,
                DebtorName = Fit(sent?.Original.Debtor.Name, 140),
                DebtorAccount = Fit(sent?.Original.Debtor.Account, 34),
                DebtorAgentBic = sent?.Original.DebtorAgent.Bic,
                CreditorName = Fit(sent?.Original.Creditor.Name, 140),
                CreditorAccount = Fit(sent?.Original.Creditor.Account, 34),
                CreditorAgentBic = sent?.Original.CreditorAgent.Bic
            };

        internal static Camt029ResolutionRow Outgoing(ValidatedCamt029? sent) =>
            new()
            {
                CancellationStatusId = sent?.CancellationStatusId,
                OriginalMessageId = sent?.OriginalMessageId,
                OriginalEndToEndId = sent?.OriginalEndToEndId,
                OriginalTransactionId = sent?.OriginalTransactionId,
                ResolutionReasonCode = sent?.ReasonCode,
                AdditionalInformation = Fit(sent?.AdditionalInformation, 256),
                Amount = sent?.OriginalAmount,
                Currency = sent?.OriginalCurrency,
                SettlementDate = sent?.Original.SettlementDate
            };

        internal static Pain002StatusReportRow Outgoing(ValidatedPain002? sent) =>
            new()
            {
                OriginalMessageId = sent?.OriginalMessageId,
                OriginalPaymentInformationId = sent?.OriginalPaymentInformationId,
                ReportReasonCode = sent?.ReasonCode,
                AdditionalInformation = Fit(sent?.AdditionalInformation, 256),
                OriginatorName = Fit(sent?.OriginatorName, 140)
            };

        internal static Pain001InitiationRow Incoming(IncomingPain001 received) =>
            new()
            {
                MessageId = Fit(received.MessageId, MessageRow.ReferenceLength),
                PaymentInformationId = Fit(received.PaymentInformationId, MessageRow.ReferenceLength),
                InstructionId = Fit(received.InstructionId, MessageRow.ReferenceLength),
                EndToEndId = Fit(received.EndToEndId, MessageRow.ReferenceLength),
                Amount = received.Amount,
                Currency = received.Currency,
                RequestedExecutionDate = received.RequestedExecutionDate,
                InitiatingPartyBic = Fit(received.InitiatingParty?.Bic, 11),
                DebtorName = Fit(received.Debtor.Name, 140),
                DebtorAccount = Fit(received.Debtor.Account, 34),
                DebtorAgentBic = Fit(received.Debtor.ParticipantBic, 11),
                CreditorName = Fit(received.Creditor?.Name, 140),
                CreditorAccount = Fit(received.Creditor?.Account, 34),
                CreditorAgentBic = Fit(received.Creditor?.ParticipantBic, 11),
                CategoryPurpose = Fit(received.CategoryPurposeCode, MessageRow.ReferenceLength),
                Purpose = Fit(received.PurposeCode, MessageRow.ReferenceLength),
                RemittanceInformation = Fit(received.Remittance?.Unstructured, MessageRow.DescriptionLength)
            };

        internal static Camt056RecallRow Incoming(IncomingCamt056 received) =>
            new()
            {
                MessageId = Fit(received.MessageId, MessageRow.ReferenceLength),
                RecallId = Fit(received.RecallId, MessageRow.ReferenceLength),
                OriginalMessageId = Fit(received.OriginalMessageId, MessageRow.ReferenceLength),
                OriginalEndToEndId = Fit(received.OriginalEndToEndId, MessageRow.ReferenceLength),
                OriginalTransactionId = Fit(received.OriginalTransactionId, MessageRow.ReferenceLength),
                OriginalAmount = received.OriginalAmount,
                OriginalCurrency = received.OriginalCurrency,
                OriginalSettlementDate = received.OriginalSettlementDate,
                RecallReasonCode = Fit(received.ReasonCode, MessageRow.ReferenceLength),
                DebtorName = Fit(received.Original.Debtor?.Name, 140),
                DebtorAccount = Fit(received.Original.Debtor?.Account, 34),
                DebtorAgentBic = Fit(received.Original.DebtorAgent?.Bic, 11),
                CreditorName = Fit(received.Original.Creditor?.Name, 140),
                CreditorAccount = Fit(received.Original.Creditor?.Account, 34),
                CreditorAgentBic = Fit(received.Original.CreditorAgent?.Bic, 11)
            };

        internal static Camt055CancellationRow Incoming(IncomingCamt055 received) =>
            new()
            {
                MessageId = Fit(received.MessageId, MessageRow.ReferenceLength),
                CancellationId = Fit(received.CancellationId, MessageRow.ReferenceLength),
                PaymentCancellationId = Fit(received.PaymentCancellationId, MessageRow.ReferenceLength),
                AssignerBic = Fit(received.AssignerBic, 11),
                AssigneeBic = Fit(received.AssigneeBic, 11),
                OriginalMessageId = Fit(received.OriginalGroup?.MessageId, MessageRow.ReferenceLength),
                OriginalPaymentInformationId = Fit(received.OriginalPaymentInformationId, MessageRow.ReferenceLength),
                OriginalInstructionId = Fit(received.OriginalInstructionId, MessageRow.ReferenceLength),
                OriginalEndToEndId = Fit(received.OriginalEndToEndId, MessageRow.ReferenceLength),
                Amount = received.Original.Amount,
                Currency = received.Original.Currency,
                RequestedExecutionDate = received.Original.RequestedExecutionDate,
                CancellationReasonCode = Fit(received.Reason?.ReasonCode, MessageRow.ReferenceLength),
                OriginatorBic = Fit(received.Reason?.OriginatorBic, 11),
                CreditorName = Fit(received.Original.Creditor?.Name, 140),
                CreditorAccount = Fit(received.Original.CreditorAccount, 34)
            };

        internal static Camt029ResolutionRow Incoming(IncomingCamt029 received) =>
            new()
            {
                MessageId = Fit(received.MessageId, MessageRow.ReferenceLength),
                CancellationStatusId = Fit(received.CancellationStatusId, MessageRow.ReferenceLength),
                OriginalMessageId = Fit(received.OriginalMessageId, MessageRow.ReferenceLength),
                OriginalEndToEndId = Fit(received.OriginalEndToEndId, MessageRow.ReferenceLength),
                OriginalTransactionId = Fit(received.OriginalTransactionId, MessageRow.ReferenceLength),
                ResolutionReasonCode = Fit(received.ReasonCode, MessageRow.ReferenceLength),
                AdditionalInformation = Fit(received.AdditionalInformation, 256),
                Amount = received.Original.Amount,
                Currency = received.Original.Currency,
                SettlementDate = received.Original.SettlementDate
            };
    }
}
