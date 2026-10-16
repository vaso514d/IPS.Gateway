using System.Globalization;
using FluentValidation;

namespace IPS.Middleware.Application.Payments.Pacs008.Validation;

internal sealed class Pacs008Validator : AbstractValidator<Pacs008Request>
{
    public Pacs008Validator(Pacs008Policy policy)
    {
        RuleFor(x => x.ClientReference).ProtocolText(Pacs008Text.HeaderText(35), required: true);
        RuleFor(x => x.InstructionId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.EndToEndId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.EndToEndId).Must(value => !IsInitiated(value) || value!.Trim().Length > 4)
            .WithMessage("The initiation prefix must be followed by the original payment identifier.");
        RuleFor(x => x.CreationDateTime).NotNull().WithMessage("Creation time is required.");
        RuleFor(x => x.AcceptanceDateTime).NotNull().WithMessage("Acceptance time is required.");
        RuleFor(x => x.AcceptanceDateTime).Custom((accepted, context) =>
        {
            var request = context.InstanceToValidate;
            if (accepted is { } acceptedAt && request.CreationDateTime is { } createdAt &&
                AcceptanceTimeError(acceptedAt, createdAt, IsInitiated(request.EndToEndId)) is { } error)
            {
                context.AddFailure(error);
            }
        });
        RuleFor(x => x.Currency).ProtocolText(Pacs008Text.Currency, required: true);
        RuleFor(x => x.Currency).Must(code => policy.FindCurrency(code) is { Enabled: true })
            .WithMessage("Currency is not enabled.");
        RuleFor(x => x.Amount).Must(amount => amount is > 0).WithMessage("A positive amount is required.");
        RuleFor(x => x.Amount).Must(amount => amount is null || PaymentChecksums.HasAllowedPrecision(amount.Value))
            .WithMessage("Amount permits 13 integer and 5 fractional digits.");
        RuleFor(x => x.Amount).Must((request, amount) => WithinCurrencyLimits(amount, policy.FindCurrency(request.Currency)))
            .WithMessage("Amount is outside the configured currency limits.");
        RuleFor(x => x.InstructionPriority).Must(value => value is "NORM" or "HIGH").WithMessage("Use NORM or HIGH.");
        RuleFor(x => x.CategoryPurposeCode).ProtocolText(Pacs008Text.Text(4));
        RuleFor(x => x.Debtor).NotNull().WithMessage("Debtor is required.");
        RuleFor(x => x.Debtor).SetValidator(new DebtorValidator(policy)!);
        RuleFor(x => x.Creditor).NotNull().WithMessage("Creditor is required.");
        RuleFor(x => x.Creditor).SetValidator(new CreditorValidator(policy)!);
        RuleFor(x => x.Creditor).Custom((creditor, context) =>
        {
            if (!string.IsNullOrWhiteSpace(creditor?.Account) &&
                string.Equals(creditor.Account.Trim(), context.InstanceToValidate.Debtor?.Account?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                context.AddFailure(nameof(Pacs008Request.Creditor) + "." + nameof(Pacs008CreditorInput.Account), "Debtor and creditor accounts must differ.");
            }
        });
        RuleFor(x => x.UltimateDebtor).SetValidator(new PartyValidator<Pacs008UltimatePartyInput>()!);
        RuleFor(x => x.UltimateCreditor).SetValidator(new PartyValidator<Pacs008UltimatePartyInput>()!);
        RuleFor(x => x.PaymentInitiation).SetValidator(new PaymentInitiationValidator()!);
        RuleFor(x => x.InitiationChannelInstrument).SetValidator(new InitiationChannelValidator()!);
        RuleFor(x => x.Remittance).SetValidator(new RemittanceValidator()!);
    }

    private static bool IsInitiated(string? value) => value?.Trim() is { } id &&
        (id.StartsWith("RTP-", StringComparison.Ordinal) || id.StartsWith("PSP-", StringComparison.Ordinal));

    // An initiated payment carries the original request time, which may precede creation; otherwise acceptance
    // follows creation by at most one second.
    private static string? AcceptanceTimeError(DateTimeOffset acceptedAt, DateTimeOffset createdAt, bool initiated)
    {
        if (initiated)
        {
            return acceptedAt > createdAt ? "Original request time cannot follow creation time." : null;
        }

        var withinOneSecond = acceptedAt >= createdAt && acceptedAt - createdAt <= TimeSpan.FromSeconds(1);
        return withinOneSecond ? null : "Acceptance must be within one second after creation.";
    }

    private static bool WithinCurrencyLimits(decimal? amount, PaymentCurrency? currency) =>
        amount is not > 0 || currency is null ||
        ((currency.MinAmount is null || amount >= currency.MinAmount) && (currency.MaxAmount is null || amount <= currency.MaxAmount));
}
