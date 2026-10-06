using FluentValidation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008.Validation;

namespace IPS.Middleware.Application.Payments.Pacs004.Validation;

// The source's content rules for an outgoing pacs.004, with the reason code narrowed to the only one IPS allows.
// Fields the IPS v1 profile does not send (originator, additional info, SWIFT codes) are still checked, so a caller
// learns about bad values even though they never reach the wire.
internal sealed class Pacs004Validator : AbstractValidator<Pacs004Request>
{
    public Pacs004Validator(Pacs008Policy policy)
    {
        RuleFor(x => x.ClientReference).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.Id).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.Amount).Must(amount => amount is > 0).WithMessage("Amount must be greater than zero.");
        RuleFor(x => x.Amount).Must(amount => amount is null || PaymentChecksums.HasAllowedPrecision(amount.Value))
            .WithMessage("Amount permits 13 integer and 5 fractional digits.");
        RuleFor(x => x.Currency).ProtocolText(Pacs008Text.Currency, required: true);
        RuleFor(x => x.Currency).Must(code => policy.FindCurrency(code) is { Enabled: true })
            .WithMessage("Currency is not enabled.");
        RuleFor(x => x.ValueDate).NotNull().WithMessage("ValueDate is required.");
        RuleFor(x => x.TransactionTypeCode).ProtocolText(Pacs008Text.SimpleText(35));
        RuleFor(x => x.InstructedAgent).ProtocolText(Pacs008Text.Bic, required: true);
        RuleFor(x => x.InstructingAgent).ProtocolText(Pacs008Text.Bic);
        RuleFor(x => x.InstructingAgent).Must(bic => string.IsNullOrWhiteSpace(bic) ||
                string.Equals(bic.Trim(), policy.ParticipantBic, StringComparison.OrdinalIgnoreCase))
            .WithMessage("The instructing agent must be our participant.");
        RuleFor(x => x.SenderIndirectParticipant).ProtocolText(Pacs008Text.Compact(35));
        RuleFor(x => x.SenderIndirectParticipant).Must(bic => string.IsNullOrWhiteSpace(bic) ||
                policy.IndirectParticipants.Contains(bic.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Indirect participant is not configured.");
        RuleFor(x => x.ReturnReasonCode).Must(code => string.IsNullOrWhiteSpace(code) || code.Trim() == ValidatedPacs004.FullReturnReason)
            .WithMessage($"Instant payments can only be returned with reason {ValidatedPacs004.FullReturnReason}.");
        RuleFor(x => x.DebtorSwift).ProtocolText(Pacs008Text.Bic);
        RuleFor(x => x.CreditorSwift).ProtocolText(Pacs008Text.Bic);
        RuleFor(x => x.Debtor).NotNull().WithMessage("Debtor is required: the original payer, who gets the money back.");
        RuleFor(x => x.Debtor).SetValidator(new PartyValidator()!);
        RuleFor(x => x.Creditor).NotNull().WithMessage("Creditor is required: the original payee, who returns the money.");
        RuleFor(x => x.Creditor).SetValidator(new PartyValidator()!);
        RuleFor(x => x.OriginatorName).ProtocolText(Pacs008Text.FreeText(140));
        RuleFor(x => x.OriginatorAddress).ProtocolText(Pacs008Text.FreeText(140));
        RuleFor(x => x.AdditionalInfo).ProtocolText(Pacs008Text.FreeText(105));
        RuleFor(x => x.Original).NotNull().WithMessage("Original is required.");
        RuleFor(x => x.Original).SetValidator(new OriginalValidator()!);
        // The original amount cannot be smaller than what is returned (Annex D 3.2.3.g); only same-currency amounts compare.
        RuleFor(x => x.Amount).Must((request, amount) => request.Original!.Amount!.Value >= amount!.Value)
            .When(request => request.Original?.Amount is not null && request.Amount is not null &&
                string.Equals(request.Original.Currency ?? request.Currency, request.Currency, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Amount cannot be greater than the original amount.");
    }

    // Name and IBAN are required (Annex D 2.40-2.63); an identifier needs its kind to choose OrgId or PrvtId.
    private sealed class PartyValidator : AbstractValidator<Pacs004PartyInput>
    {
        public PartyValidator()
        {
            RuleFor(x => x.Name).ProtocolText(Pacs008Text.FreeText(140), required: true);
            RuleFor(x => x.Account).Iban();
            RuleFor(x => x.Type).Must(type => type is null or (int)PaymentPartyKind.Organisation or (int)PaymentPartyKind.Individual)
                .WithMessage("Use 0 for an organisation or 1 for an individual.");
            RuleFor(x => x.Type).NotNull().When(x => !string.IsNullOrWhiteSpace(x.Identifier))
                .WithMessage("A type is required with an identifier.");
            RuleFor(x => x.Identifier).ProtocolText(Pacs008Text.Identification(256));
        }
    }

    private sealed class OriginalValidator : AbstractValidator<Pacs004OriginalInput>
    {
        public OriginalValidator()
        {
            RuleFor(x => x.TransactionId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
            RuleFor(x => x.InstructionId).ProtocolText(Pacs008Text.AsciiId(35));
            RuleFor(x => x.EndToEndId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
            RuleFor(x => x.ValueDate).NotNull().WithMessage("The original value date is required.");
            RuleFor(x => x.Amount).Must(amount => amount is null or > 0).WithMessage("The original amount must be greater than zero.");
            RuleFor(x => x.Amount).Must(amount => amount is null || PaymentChecksums.HasAllowedPrecision(amount.Value))
                .WithMessage("Amount permits 13 integer and 5 fractional digits.");
            RuleFor(x => x.Currency).ProtocolText(Pacs008Text.Currency);
            // A currency without its amount would label the returned amount as the original.
            RuleFor(x => x.Amount).NotNull().When(x => !string.IsNullOrWhiteSpace(x.Currency))
                .WithMessage("The original amount is required with its currency.");
            RuleFor(x => x.Uetr).Must(uetr => uetr is null || uetr != Guid.Empty).WithMessage("UETR must not be an empty GUID.");
            RuleFor(x => x.OriginalMessageId).ProtocolText(Pacs008Text.AsciiId(35));
            RuleFor(x => x.OriginalMessageNameId).ProtocolText(Pacs008Text.AsciiId(35));
        }
    }
}
