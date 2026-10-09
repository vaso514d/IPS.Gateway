using FluentValidation;

namespace IPS.Middleware.Application.Payments.Pacs008.Validation;

internal sealed class PartyValidator<T> : AbstractValidator<T> where T : Pacs008PartyInput
{
    public PartyValidator(Func<T, string?>? participantBic = null)
    {
        RuleFor(x => x.Type).Must(type => type is (int)PaymentPartyKind.Organisation or (int)PaymentPartyKind.Individual)
            .WithMessage("Use 0 for an organisation or 1 for an individual.");
        RuleFor(x => x.Name).ProtocolText(Pacs008Text.Text(140), required: true);
        RuleFor(x => x.Identifier).ProtocolText(Pacs008Text.Text(256));
        RuleFor(x => x.Identifier).Must(PaymentChecksums.ValidTaxCode)
            .When(party => party.Type == (int)PaymentPartyKind.Organisation && IsResident(participantBic?.Invoke(party)))
            .WithMessage("A resident organisation requires a valid nine-digit tax code.");
    }

    private static bool IsResident(string? bic) => bic is { Length: >= 6 } &&
        bic.AsSpan(4, 2).Equals("GE", StringComparison.OrdinalIgnoreCase);
}

internal sealed class DebtorValidator : AbstractValidator<Pacs008DebtorInput>
{
    public DebtorValidator(Pacs008Policy policy)
    {
        Include(new PartyValidator<Pacs008DebtorInput>(_ => policy.ParticipantBic));
        RuleFor(x => x.BillIdentifier).ProtocolText(Pacs008Text.Text(256));
        RuleFor(x => x.Account).Iban();
        RuleFor(x => x.Address).SetValidator(new PostalAddressValidator()!);
        RuleFor(x => x.IndirectParticipantBic).ProtocolText(Pacs008Text.Text(35));
        RuleFor(x => x.IndirectParticipantBic).Must(bic => string.IsNullOrWhiteSpace(bic) ||
            policy.IndirectParticipants.Contains(bic.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Indirect payer is not configured.");
        RuleFor(x => x.ParticipantBic).Must(bic => string.IsNullOrWhiteSpace(bic) ||
            string.Equals(bic.Trim(), policy.ParticipantBic, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Debtor participant must be our own participant.");
    }
}

internal sealed class CreditorValidator : AbstractValidator<Pacs008CreditorInput>
{
    public CreditorValidator(Pacs008Policy policy)
    {
        Include(new PartyValidator<Pacs008CreditorInput>(x => x.ParticipantBic));
        RuleFor(x => x.ParticipantBic).ProtocolText(Pacs008Text.Bic, required: true);
        RuleFor(x => x.IndirectParticipantBic).ProtocolText(Pacs008Text.Text(35));
        RuleFor(x => x.Address).SetValidator(new PostalAddressValidator()!);
        When(x => policy.IsTreasury(x.ParticipantBic), () =>
            RuleFor(x => x.Account).ProtocolText(Pacs008Text.Text(34), required: true))
            .Otherwise(() => RuleFor(x => x.Account).Iban());
        RuleFor(x => x.ParticipantBic).Must(bic => !string.Equals(bic?.Trim(), policy.ParticipantBic, StringComparison.OrdinalIgnoreCase))
            .WithMessage("The receiving bank must differ from our participant.");
    }
}

internal sealed class PostalAddressValidator : AbstractValidator<Pacs008PostalAddressInput>
{
    public PostalAddressValidator()
    {
        RuleFor(x => x.StreetName).ProtocolText(Pacs008Text.Text(140));
        RuleFor(x => x.BuildingNumber).ProtocolText(Pacs008Text.Text(16));
        RuleFor(x => x.PostCode).ProtocolText(Pacs008Text.Text(16));
        RuleFor(x => x.TownName).ProtocolText(Pacs008Text.Text(140));
        RuleFor(x => x.CountrySubdivision).ProtocolText(Pacs008Text.Text(35));
        RuleFor(x => x.Country).ProtocolText("^[A-Z]{2}$");
        RuleFor(x => x.AddressLines).ProtocolText(Pacs008Text.Text(490));
    }
}
