using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt055;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain001;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.MiidleWear.Testing;

// One delivery of each receive kind as the gateway sends it, with identifiers unique to the run.
internal sealed record SampleMessage(string Kind, IpsMessageKind MessageKind, string Route, object Body, string Key, ExpectedEcho Echo);

internal static class SampleMessages
{
    private const string OtherBank = "TBCBGE22";

    internal static IReadOnlyList<SampleMessage> All(string run, IpsReceiverConformanceOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var us = options.ParticipantBic;
        var account = options.CreditAccount;

        var pacs004 = new Pacs004PaymentReturnRequestDto
        {
            Id = $"KIT-RT-{run}",
            Amount = 10.00m,
            Currency = options.Currency,
            ValueDate = today,
            InstructingAgent = OtherBank,
            InstructedAgent = us,
            ReturnReasonCode = "FOCR",
            Creditor = new() { Name = "Kit Payer", Account = account },
            Original = new() { TransactionId = $"KIT-OT-{run}", EndToEndId = $"KIT-OE-{run}", ValueDate = today.AddDays(-1), Amount = 10.00m, Currency = options.Currency }
        };
        var pain001 = new Pain001PaymentInitiationDto
        {
            MessageId = $"KIT-PM-{run}",
            CreationDateTime = now,
            PaymentInformationId = $"KIT-PI-{run}",
            InitiatingParty = new() { Name = "Kit PISP", Bic = "PISPGE22" },
            Amount = 25.00m,
            Currency = options.Currency,
            EndToEndId = $"KIT-PE-{run}",
            Debtor = new() { ParticipantBic = us, Type = 0, Name = "Kit Payer", Account = account },
            Creditor = new() { ParticipantBic = OtherBank, Type = 0, Name = "Kit Shop", Account = "GE29TB0000000101904917" }
        };
        var camt056 = new Camt056RecallRequestDto
        {
            Id = $"KIT-RC-{run}",
            CreDtTm = now,
            RecallId = $"KIT-CX-{run}",
            OriginalMessageId = $"KIT-OM-{run}",
            OriginalEndToEndId = $"KIT-OE-{run}",
            OriginalTransactionId = $"KIT-OT-{run}",
            OriginalAmount = 10.00m,
            OriginalCurrency = options.Currency,
            OriginalSettlementDate = today.AddDays(-1),
            ReasonCode = "DUPL",
            OriginalTransaction = new() { DebtorAgent = new() { Bicfi = OtherBank }, CreditorAgent = new() { Bicfi = us } }
        };
        var camt055 = new Camt055CancellationRequestDto
        {
            MsgId = $"KIT-CN-{run}",
            AssignerBic = "PISPGE22",
            AssigneeBic = us,
            CreDtTm = now.UtcDateTime,
            OriginalPaymentInformationId = $"KIT-PI-{run}",
            CancellationReason = new() { ReasonCode = "CUST", AdditionalInformation = "Customer asked to cancel" },
            OriginalTransaction = new() { Currency = options.Currency, Amount = 25.00m, DebtorAgent = new() { Bicfi = us } }
        };
        var camt029 = new Camt029ResolutionOfInvestigationDto
        {
            ClientReference = $"KIT-RECALL-{run}",
            Id = $"KIT-RF-{run}",
            CreDtTm = now,
            CancellationStatusId = $"KIT-CS-{run}",
            OriginalMessageId = $"KIT-OR-{run}",
            OriginalEndToEndId = $"KIT-OE-{run}",
            OriginalTransactionId = $"KIT-OT-{run}",
            ReasonCode = "CUST",
            OriginalTransaction = new() { DebtorAgent = new() { Bicfi = us }, CreditorAgent = new() { Bicfi = OtherBank } }
        };

        return
        [
            new("pacs.004", IpsMessageKind.Pacs004, Pacs004RestApiRoutes.Receive, pacs004, IpsIdempotencyKeys.For(pacs004)!,
                new(null, pacs004.Id, null, AbsentWhenUnknown: false)),
            new("pain.001", IpsMessageKind.Pain001, Pain001RestApiRoutes.Receive, pain001, IpsIdempotencyKeys.For(pain001)!,
                new(null, pain001.PaymentInformationId, null, AbsentWhenUnknown: false)),
            new("camt.056", IpsMessageKind.Camt056, Camt056RestApiRoutes.Receive, camt056, IpsIdempotencyKeys.For(camt056)!,
                new(null, camt056.Id, null, AbsentWhenUnknown: false)),
            new("camt.055", IpsMessageKind.Camt055, Camt055RestApiRoutes.Receive, camt055, IpsIdempotencyKeys.For(camt055)!,
                new(null, camt055.MsgId, null, AbsentWhenUnknown: false)),
            new("camt.029", IpsMessageKind.Camt029, Camt029RestApiRoutes.Receive, camt029, IpsIdempotencyKeys.For(camt029)!,
                new(null, camt029.Id, null, AbsentWhenUnknown: false))
        ];
    }

    // The incoming instant payment, in its own envelope: the gateway's transactionId is the key and the status reference.
    internal static IncomingPaymentRequestDto IncomingPayment(string run, IpsReceiverConformanceOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        return new()
        {
            TransactionId = $"KIT-TX8-{run}",
            OperationType = IncomingOperationTypes.InstantPayment,
            Data = new()
            {
                MessageId = $"KIT-M8-{run}",
                InstructionId = $"KIT-I8-{run}",
                EndToEndId = $"KIT-E8-{run}",
                IsoTransactionId = $"KIT-T8-{run}",
                ReceivedAt = now,
                CreationDateTime = now,
                AcceptanceDateTime = now,
                Amount = 10.00m,
                Currency = options.Currency,
                InstructionPriority = "NORM",
                Debtor = new()
                {
                    Name = "Kit Payer",
                    PartyType = 1,
                    Identifier = "01001000001",
                    ParticipantBic = OtherBank,
                    Account = new() { Type = IncomingAccountTypes.Iban, Iban = "GE29TB0000000101904917" }
                },
                Creditor = new()
                {
                    Name = "Kit Payee",
                    Account = new() { Type = IncomingAccountTypes.Iban, Iban = options.CreditAccount }
                },
                Remittance = new() { Unstructured = "Receiver conformance check" }
            }
        };
    }

    // The incoming FI transfer, in its own envelope: the gateway's transactionId is the key and the status reference.
    internal static IncomingFiTransferRequestDto FiTransfer(string run, IpsReceiverConformanceOptions options) => new()
    {
        TransactionId = $"KIT-TX9-{run}",
        OperationType = IncomingOperationTypes.FiTransfer,
        Data = new()
        {
            MessageId = $"KIT-M9-{run}",
            InstructionId = $"KIT-I9-{run}",
            EndToEndId = $"KIT-E9-{run}",
            IsoTransactionId = $"KIT-T9-{run}",
            ReceivedAt = DateTimeOffset.UtcNow,
            Amount = 1000.00m,
            Currency = options.Currency,
            SettlementDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Debtor = new() { ParticipantBic = OtherBank },
            Creditor = new() { ParticipantBic = options.ParticipantBic, AccountIban = options.CreditAccount }
        }
    };

    internal static TransactionStatusDto OutgoingStatus(string run) => new()
    {
        MessageKind = IpsMessageKind.Pacs008,
        ClientReference = $"KIT-CLIENT-{run}",
        Direction = TransactionDirection.Outgoing,
        TransactionId = Guid.NewGuid(),
        Status = TransactionStatus.Accepted,
        StatusAtUtc = DateTimeOffset.UtcNow,
        EndToEndId = $"KIT-OUT-{run}"
    };

    internal static TransactionStatusDto Reversal(string run) => new()
    {
        MessageKind = IpsMessageKind.Pacs008,
        Direction = TransactionDirection.Incoming,
        CoreReference = $"KIT-POSTING-{run}",
        TransactionId = Guid.NewGuid(),
        Status = TransactionStatus.Rejected,
        StatusAtUtc = DateTimeOffset.UtcNow,
        Description = "Credited after IPS was told RJCT; reverse the posting."
    };
}
