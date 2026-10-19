using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt055;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain001;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.MiidleWear.AspNetCore;

// Typed examples of what the gateway sends and the answer it reads, serialized into the receiver document with
// IpsJson.SerializerOptions. The values follow the Contracts wire cases, so the document shows real wire shapes.
internal static class ReceiverExamples
{
    internal static readonly Pacs008InstantPaymentRequestDto Pacs008 = new()
    {
        ClientReference = "CBS-008",
        EndToEndId = "E2E-008",
        Amount = 125.5m,
        Currency = "GEL",
        Debtor = new() { ParticipantBic = "BAGAGE22", Name = "Test Debtor", Account = "GE00TEST00000000000000" },
        Creditor = new() { Name = "Test Creditor", ParticipantBic = "TBCBGE22", Account = "GE00TEST00000000000001" },
        InitiationChannelInstrument = new() { ChannelCode = "MOBL", InstrumentCodes = ["PRXY"] },
        Remittance = new()
        {
            Unstructured = "Test payment",
            Structured = [new() { ReferenceType = "MCC", Reference = "5411" }]
        }
    };

    internal static readonly Pacs009PaymentRequestDto Pacs009 = new()
    {
        ClientReference = "CBS-009",
        Id = "MSG-009",
        DebtorAgent = new() { Bicfi = "BAGAGE22" },
        CreditorAgent = new() { Bicfi = "TBCBGE22" },
        EndToEndId = "E2E-009",
        TxId = "TX-009",
        Uetr = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        FromTime = new TimeOnly(9, 30),
        ValueDate = new DateOnly(2026, 10, 3),
        Currency = "GEL",
        Amount = 500m
    };

    internal static readonly Pacs004PaymentReturnRequestDto Pacs004 = new()
    {
        ClientReference = "CBS-004",
        Id = "RETURN-004",
        Amount = 125.5m,
        Currency = "GEL",
        ValueDate = new DateOnly(2026, 10, 3),
        ReturnReasonCode = "AC04",
        Original = new()
        {
            TransactionId = "TX-008",
            EndToEndId = "E2E-008",
            ValueDate = new DateOnly(2026, 10, 3),
            OriginalMessageId = "MSG-008",
            OriginalMessageNameId = "pacs.008.001.12",
            Amount = 125.5m,
            Currency = "GEL"
        }
    };

    internal static readonly Pain001PaymentInitiationDto Pain001 = new()
    {
        ClientReference = "CBS-001",
        MessageId = "MSG-001",
        CreationDateTime = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(4)),
        InitiatingParty = new() { Name = "Test PISP", Bic = "TBCBGE22" },
        PaymentInformationId = "PISP-1",
        RequestedExecutionDate = new DateOnly(2026, 10, 3),
        EndToEndId = "INIT-1",
        Amount = 100m,
        Currency = "GEL"
    };

    internal static readonly Camt056RecallRequestDto Camt056 = new()
    {
        Id = "IN-RCL-1",
        CreDtTm = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero),
        RecallId = "IN-CXL-1",
        OriginalMessageId = "P008-MSG-1",
        OriginalEndToEndId = "E2E-RCL-1",
        OriginalTransactionId = "TX-RCL-1",
        OriginalCurrency = "GEL",
        OriginalAmount = 150.25m,
        OriginalSettlementDate = new DateOnly(2026, 10, 7),
        ReasonCode = "DUPL",
        OriginalTransaction = new()
        {
            SettlementDate = new DateOnly(2026, 10, 7),
            RemittanceInformation = new() { Unstructured = "Invoice 9", CreditorReference = new() { Issuer = "ISS", Reference = "REF-9" } },
            Debtor = new()
            {
                Name = "Original Payer",
                PostalAddress = new() { TownName = "Tbilisi", Country = "GE", AddressLines = ["Rustaveli 1"] },
                Type = 1,
                Id = "01001",
                Account = "GE95TB0000000123456789"
            },
            DebtorAgent = new() { Bicfi = "TBCBGE22" },
            CreditorAgent = new() { Bicfi = "BAGAGE22", Name = "Bank of Georgia" },
            Creditor = new() { Name = "Original Payee", Account = "GE29NB0000000101904917" }
        }
    };

    internal static readonly Camt055CancellationRequestDto Camt055 = new()
    {
        MsgId = "IN-CNCL-1",
        AssignerBic = "PISPGE22",
        AssigneeBic = "NBGEGE22",
        CreDtTm = new DateTime(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc),
        PaymentCancellationId = "PCXL-1",
        OriginalPaymentInformationId = "PMTINF-1",
        OriginalGroupInformation = new()
        {
            OriginalMessageId = "PAIN001-MSG-1",
            OriginalMessageNameId = "pain.001.001.12",
            OriginalCreationDateTime = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc)
        },
        CancellationId = "CXL-1",
        OriginalInstructionId = "INS-1",
        OriginalEndToEndId = "PISP-E2E-1",
        CancellationReason = new()
        {
            OriginatorName = "PISP LLC",
            OriginatorBic = "PISPGE22",
            ReasonCode = "CUST",
            AdditionalInformation = "Customer asked"
        },
        OriginalTransaction = new()
        {
            Currency = "GEL",
            Amount = 25.5m,
            RequestedExecutionDate = new DateTime(2026, 10, 8),
            ServiceLevelCode = "INST",
            LocalInstrumentCode = "INST",
            CategoryPurposeCode = "OTHR",
            RemittanceInformationUnstructured = "Invoice 77",
            DebtorAgent = new() { Bicfi = "BAGAGE22" },
            CreditorAgent = new() { Bicfi = "TBCBGE22" },
            Creditor = new() { Name = "Receiver LLC", Id = "400000002" },
            CreditorAccount = new() { Iban = "GE29NB0000000101904917" }
        }
    };

    internal static readonly Camt029ResolutionOfInvestigationDto Camt029 = new()
    {
        ClientReference = "CBS-RCL-1",
        Id = "IN-RJCR-1",
        CreDtTm = new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero),
        CancellationStatusId = "IN-CXLSTS-1",
        OriginalMessageId = "RCL-OUT-1",
        OriginalEndToEndId = "E2E-OUT-1",
        OriginalTransactionId = "TX-OUT-1",
        ReasonCode = "NOAS",
        AdditionalInformation = "No answer from customer",
        OriginalTransaction = new()
        {
            Currency = "GEL",
            Amount = 100m,
            SettlementDate = new DateOnly(2026, 10, 7),
            Debtor = new() { Name = "Our Payer", Account = "GE95TB0000000123456789" },
            DebtorAgent = new() { Bicfi = "BAGAGE22" },
            CreditorAgent = new() { Bicfi = "TBCBGE22" },
            Creditor = new() { Name = "Their Payee", Account = "GE29NB0000000101904917" }
        }
    };

    internal static readonly TransactionStatusDto TransactionStatus = new()
    {
        MessageKind = IpsMessageKind.Pacs008,
        ClientReference = "CBS-008",
        Direction = TransactionDirection.Outgoing,
        TransactionId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Status = Contracts.Transactions.TransactionStatus.Accepted,
        StatusAtUtc = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(4)),
        MessageId = "MSG-008",
        EndToEndId = "E2E-008"
    };

    internal static readonly Pacs008PaymentResultDto Answer = new()
    {
        Status = Pacs008PaymentStatuses.Accepted,
        Description = "Posted",
        ProcessedAtUtc = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(4))
    };
}
