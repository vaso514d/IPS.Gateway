using FluentValidation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008.Validation;

namespace IPS.Middleware.Application.Payments.Camt056.Validation;

// The source's content rules for an outgoing camt.056 (Annex D 3.2.4): lengths follow the XSD, the debtor agent must be
// our participant and the settlement dates may not lie in the future. Nothing is checked against the recalled payment.
internal sealed class Camt056Validator : AbstractValidator<Camt056Request>
{
    public Camt056Validator(Pacs008Policy policy, DateOnly today)
    {
        RuleFor(x => x.ClientReference).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.Id).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.RecallId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.OriginalMessageId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.OriginalEndToEndId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.OriginalTransactionId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.OriginalCurrency).ProtocolText(Pacs008Text.Currency, required: true);
        RuleFor(x => x.OriginalCurrency).Must(code => policy.FindCurrency(code) is { Enabled: true })
            .WithMessage("Currency is not enabled.");
        RuleFor(x => x.OriginalAmount).Must(amount => amount is > 0).WithMessage("The original amount must be greater than zero.");
        RuleFor(x => x.OriginalAmount).Must(amount => amount is null || PaymentChecksums.HasAllowedPrecision(amount.Value))
            .WithMessage("Amount permits 13 integer and 5 fractional digits.");
        RuleFor(x => x.OriginalSettlementDate).NotNull().WithMessage("The original settlement date is required.");
        RuleFor(x => x.OriginalSettlementDate).Must(date => date is null || date <= today)
            .WithMessage("The original settlement date must not be in the future.");
        RuleFor(x => x.ReasonCode).ProtocolText("^[A-Z0-9]{1,4}$", required: true);
        RuleFor(x => x.OriginalTransaction).NotNull().WithMessage("The original transaction is required.");
        RuleFor(x => x.OriginalTransaction).SetValidator(new OriginalValidator(policy, today)!);
    }

    private sealed class OriginalValidator : AbstractValidator<RecallOriginalInput>
    {
        public OriginalValidator(Pacs008Policy policy, DateOnly today)
        {
            RuleFor(x => x.SettlementDate).NotNull().WithMessage("The settlement date is required.");
            RuleFor(x => x.SettlementDate).Must(date => date is null || date <= today)
                .WithMessage("The settlement date must not be in the future.");
            RuleFor(x => x.Remittance).SetValidator(new RemittanceValidator()!);
            RuleFor(x => x.UltimateDebtor).SetValidator(new UltimatePartyValidator()!);
            RuleFor(x => x.UltimateCreditor).SetValidator(new UltimatePartyValidator()!);
            RuleFor(x => x.Debtor).NotNull().WithMessage("The debtor is required.");
            RuleFor(x => x.Debtor).SetValidator(new PartyValidator()!);
            RuleFor(x => x.Creditor).NotNull().WithMessage("The creditor is required.");
            RuleFor(x => x.Creditor).SetValidator(new PartyValidator()!);
            RuleFor(x => x.DebtorAgent).NotNull().WithMessage("The debtor agent is required.");
            RuleFor(x => x.DebtorAgent).SetValidator(new AgentValidator()!);
            RuleFor(x => x.CreditorAgent).NotNull().WithMessage("The creditor agent is required.");
            RuleFor(x => x.CreditorAgent).SetValidator(new AgentValidator()!);
            // Annex D 3.2.4.f: the recall comes from the debtor's participant, which is us.
            RuleFor(x => x.DebtorAgent!.Bic).Must(bic => string.Equals(bic?.Trim(), policy.ParticipantBic, StringComparison.OrdinalIgnoreCase))
                .When(x => !string.IsNullOrWhiteSpace(x.DebtorAgent?.Bic))
                .WithMessage("The debtor agent must be our participant.");
        }
    }

    // Name is required; an identifier needs its type to choose OrgId or PrvtId; the account is an IBAN.
    private sealed class PartyValidator : AbstractValidator<RecallPartyInput>
    {
        public PartyValidator()
        {
            RuleFor(x => x.Name).ProtocolText(Pacs008Text.FreeText(140), required: true);
            RuleFor(x => x.Type).Must(type => type is null or (int)PaymentPartyKind.Organisation or (int)PaymentPartyKind.Individual)
                .WithMessage("Use 0 for an organisation or 1 for an individual.");
            RuleFor(x => x.Type).NotNull().When(x => !string.IsNullOrWhiteSpace(x.Identifier))
                .WithMessage("A type is required with an identifier.");
            RuleFor(x => x.Identifier).ProtocolText(Pacs008Text.Identification(256));
            RuleFor(x => x.Account).Iban();
            RuleFor(x => x.Address).SetValidator(new AddressValidator()!);
        }
    }

    private sealed class UltimatePartyValidator : AbstractValidator<RecallUltimatePartyInput>
    {
        public UltimatePartyValidator()
        {
            RuleFor(x => x.Name).ProtocolText(Pacs008Text.FreeText(140), required: true);
            RuleFor(x => x.Type).Must(type => type is null or (int)PaymentPartyKind.Organisation or (int)PaymentPartyKind.Individual)
                .WithMessage("Use 0 for an organisation or 1 for an individual.");
            RuleFor(x => x.Type).NotNull().When(x => !string.IsNullOrWhiteSpace(x.Identifier))
                .WithMessage("A type is required with an identifier.");
            RuleFor(x => x.Identifier).ProtocolText(Pacs008Text.Identification(256));
        }
    }

    private sealed class AddressValidator : AbstractValidator<RecallAddressInput>
    {
        public AddressValidator()
        {
            RuleFor(x => x.StreetName).ProtocolText(Pacs008Text.FreeText(140));
            RuleFor(x => x.BuildingNumber).ProtocolText(Pacs008Text.FreeText(16));
            RuleFor(x => x.PostCode).ProtocolText(Pacs008Text.FreeText(16));
            RuleFor(x => x.TownName).ProtocolText(Pacs008Text.FreeText(140));
            RuleFor(x => x.CountrySubdivision).ProtocolText(Pacs008Text.FreeText(35));
            RuleFor(x => x.Country).ProtocolText("^[A-Z]{2}$");
            RuleFor(x => x.AddressLines).Must(lines => lines is null || lines.Count(line => !string.IsNullOrWhiteSpace(line)) <= 7).WithMessage("At most seven address lines are allowed.");
            RuleForEach(x => x.AddressLines).ProtocolText(Pacs008Text.FreeText(70));
        }
    }

    private sealed class AgentValidator : AbstractValidator<RecallAgentInput>
    {
        public AgentValidator()
        {
            RuleFor(x => x.Bic).ProtocolText(Pacs008Text.Bic, required: true);
            RuleFor(x => x.Name).ProtocolText(Pacs008Text.FreeText(140));
        }
    }

    private sealed class RemittanceValidator : AbstractValidator<RecallRemittanceInput>
    {
        public RemittanceValidator()
        {
            RuleFor(x => x.Unstructured).ProtocolText(Pacs008Text.FreeText(140));
            RuleFor(x => x.CreditorReference!.Reference).ProtocolText(Pacs008Text.FreeText(35), required: true)
                .When(x => x.CreditorReference is not null);
            RuleFor(x => x.CreditorReference!.Issuer).ProtocolText(Pacs008Text.FreeText(35))
                .When(x => x.CreditorReference is not null);
        }
    }
}
