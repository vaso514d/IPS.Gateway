using FluentValidation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008.Validation;

namespace IPS.Middleware.Application.Payments.Recalls.Validation;

// The participant that sends the message: a recall comes from the debtor's participant (Annex D 3.2.4.f), the negative
// answer from the creditor's (3.2.5.g). That agent must be us.
internal enum RecallSender
{
    DebtorAgent,
    CreditorAgent
}

// The source's content rules for the recalled payment of a camt.056 or camt.029: lengths follow the XSD and the settlement
// date may not lie in the future. Nothing is checked against the recalled payment itself. A message that quotes more of the
// payment derives from the input and the validator.
internal class RecallOriginalValidator<T> : AbstractValidator<T>
    where T : RecallOriginalInput
{
    public RecallOriginalValidator(Pacs008Policy policy, DateOnly today, RecallSender sender)
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
        if (sender == RecallSender.DebtorAgent)
        {
            RuleFor(x => x.DebtorAgent!.Bic).Must(bic => IsParticipant(bic, policy))
                .When(x => !string.IsNullOrWhiteSpace(x.DebtorAgent?.Bic))
                .WithMessage("The debtor agent must be our participant.");
        }
        else
        {
            RuleFor(x => x.CreditorAgent!.Bic).Must(bic => IsParticipant(bic, policy))
                .When(x => !string.IsNullOrWhiteSpace(x.CreditorAgent?.Bic))
                .WithMessage("The creditor agent must be our participant.");
        }
    }

    private static bool IsParticipant(string? bic, Pacs008Policy policy) =>
        string.Equals(bic?.Trim(), policy.ParticipantBic, StringComparison.OrdinalIgnoreCase);

    // Name is required; an identifier needs its type to choose OrgId or PrvtId; the account is an IBAN.
    private sealed class PartyValidator : AbstractValidator<RecallPartyInput>
    {
        public PartyValidator()
        {
            RuleFor(x => x.Name).ProtocolText(Pacs008Text.Text(140), required: true);
            RuleFor(x => x.Type).Must(type => type is null or (int)PaymentPartyKind.Organisation or (int)PaymentPartyKind.Individual)
                .WithMessage("Use 0 for an organisation or 1 for an individual.");
            RuleFor(x => x.Type).NotNull().When(x => !string.IsNullOrWhiteSpace(x.Identifier))
                .WithMessage("A type is required with an identifier.");
            RuleFor(x => x.Identifier).ProtocolText(Pacs008Text.Text(256));
            RuleFor(x => x.Account).Iban();
            RuleFor(x => x.Address).SetValidator(new AddressValidator()!);
        }
    }

    private sealed class UltimatePartyValidator : AbstractValidator<RecallUltimatePartyInput>
    {
        public UltimatePartyValidator()
        {
            RuleFor(x => x.Name).ProtocolText(Pacs008Text.Text(140), required: true);
            RuleFor(x => x.Type).Must(type => type is null or (int)PaymentPartyKind.Organisation or (int)PaymentPartyKind.Individual)
                .WithMessage("Use 0 for an organisation or 1 for an individual.");
            RuleFor(x => x.Type).NotNull().When(x => !string.IsNullOrWhiteSpace(x.Identifier))
                .WithMessage("A type is required with an identifier.");
            RuleFor(x => x.Identifier).ProtocolText(Pacs008Text.Text(256));
        }
    }

    private sealed class AddressValidator : AbstractValidator<RecallAddressInput>
    {
        public AddressValidator()
        {
            RuleFor(x => x.StreetName).ProtocolText(Pacs008Text.Text(140));
            RuleFor(x => x.BuildingNumber).ProtocolText(Pacs008Text.Text(16));
            RuleFor(x => x.PostCode).ProtocolText(Pacs008Text.Text(16));
            RuleFor(x => x.TownName).ProtocolText(Pacs008Text.Text(140));
            RuleFor(x => x.CountrySubdivision).ProtocolText(Pacs008Text.Text(35));
            RuleFor(x => x.Country).ProtocolText("^[A-Z]{2}$");
            RuleFor(x => x.AddressLines).Must(lines => lines is null || lines.Count(line => !string.IsNullOrWhiteSpace(line)) <= 7)
                .WithMessage("At most seven address lines are allowed.");
            RuleForEach(x => x.AddressLines).ProtocolText(Pacs008Text.Text(70));
        }
    }

    private sealed class AgentValidator : AbstractValidator<RecallAgentInput>
    {
        public AgentValidator()
        {
            RuleFor(x => x.Bic).ProtocolText(Pacs008Text.Bic, required: true);
            RuleFor(x => x.Name).ProtocolText(Pacs008Text.Text(140));
        }
    }

    private sealed class RemittanceValidator : AbstractValidator<RecallRemittanceInput>
    {
        public RemittanceValidator()
        {
            RuleFor(x => x.Unstructured).ProtocolText(Pacs008Text.Text(140));
            RuleFor(x => x.CreditorReference!.Reference).ProtocolText(Pacs008Text.Text(35), required: true)
                .When(x => x.CreditorReference is not null);
            RuleFor(x => x.CreditorReference!.Issuer).ProtocolText(Pacs008Text.Text(35))
                .When(x => x.CreditorReference is not null);
        }
    }
}
