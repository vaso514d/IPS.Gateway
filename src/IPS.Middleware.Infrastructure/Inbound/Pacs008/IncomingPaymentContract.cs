using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

// The synchronous delivery of an incoming pacs.008 to the core system (specification 022): the gateway's transactionId,
// the operation type and the payment with the IPS references and the receipt time stored with it.
public static class IncomingPaymentContract
{
    // The payment's identity in the gateway, fixed for its life: the idempotency key and the status reference.
    public static string TransactionId(Guid paymentId) => paymentId.ToString("D");

    public static IncomingPaymentRequestDto Map(IncomingProcessingSnapshot snapshot, BankIdentity bank)
    {
        var payment = snapshot.Request;
        var original = snapshot.Context.Original;
        return new()
        {
            TransactionId = TransactionId(snapshot.Payment.Id),
            OperationType = IncomingOperationTypes.InstantPayment,
            Data = new()
            {
                MessageId = original.GroupMessageId,
                InstructionId = payment.InstructionId,
                EndToEndId = payment.EndToEndId,
                IsoTransactionId = original.TransactionId,
                ReceivedAt = snapshot.Context.ReceivedAtUtc,
                CreationDateTime = payment.CreationDateTime,
                AcceptanceDateTime = payment.AcceptanceDateTime,
                Amount = payment.Amount,
                Currency = payment.Currency,
                InstructionPriority = payment.InstructionPriority,
                CategoryPurposeCode = payment.CategoryPurposeCode,
                Debtor = Debtor(payment.Debtor, bank),
                Creditor = Creditor(payment.Creditor, bank),
                UltimateDebtor = Ultimate(payment.UltimateDebtor),
                UltimateCreditor = Ultimate(payment.UltimateCreditor),
                PaymentInitiation = payment.PaymentInitiation is not { } initiation ? null : new()
                { ChannelCode = initiation.ChannelCode, Geolocation = initiation.Geolocation },
                InitiationChannelInstrument = payment.InitiationChannelInstrument is not { } instrument ? null : new()
                { ChannelCode = instrument.ChannelCode, InstrumentCodes = instrument.InstrumentCodes, ElectronicAddress = instrument.ElectronicAddress },
                Remittance = IncomingPacs008CoreMapping.Remittance(payment.Remittance)
            }
        };
    }

    private static IncomingDebtorDto? Debtor(Pacs008DebtorInput? value, BankIdentity bank) => value is null ? null : new()
    {
        Name = value.Name,
        PartyType = value.Type,
        Identifier = value.Identifier,
        BillIdentifier = value.BillIdentifier,
        Address = IncomingPacs008CoreMapping.Address(value.Address),
        ParticipantBic = value.ParticipantBic,
        IndirectParticipantBic = value.IndirectParticipantBic,
        Account = Account(value.Account, bank.IsTreasury(value.ParticipantBic))
    };

    // The creditor is a customer of this bank, so its account is a Treasury code only when this bank is the Treasury.
    private static IncomingCreditorDto? Creditor(Pacs008CreditorInput? value, BankIdentity bank) => value is null ? null : new()
    {
        Name = value.Name,
        PartyType = value.Type,
        Identifier = value.Identifier,
        Address = IncomingPacs008CoreMapping.Address(value.Address),
        IndirectParticipantBic = value.IndirectParticipantBic,
        Account = Account(value.Account, bank.IsTreasury(bank.Bic))
    };

    private static IncomingUltimatePartyDto? Ultimate(Pacs008UltimatePartyInput? value) => value is null ? null : new()
    {
        Name = value.Name,
        PartyType = value.Type,
        Identifier = value.Identifier
    };

    // An account is a Treasury code (…Acct/Id/Othr/Id) exactly when its bank is the Treasury, and an IBAN otherwise: the
    // rule the send applies (Pacs008Policy), never the form of the value.
    private static IncomingAccountDto? Account(string? account, bool treasury) => account is null ? null
        : treasury
            ? new() { Type = IncomingAccountTypes.TreasuryCode, TreasuryCode = account }
            : new() { Type = IncomingAccountTypes.Iban, Iban = account };
}
