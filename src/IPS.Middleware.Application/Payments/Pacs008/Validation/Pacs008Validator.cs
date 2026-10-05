using System.Globalization;
using FluentValidation;

namespace IPS.Middleware.Application.Payments.Pacs008.Validation;

internal sealed class Pacs008Validator : AbstractValidator<Pacs008Request>
{
    public Pacs008Validator(Pacs008Policy policy)
    {
        RuleFor(x => x.ClientReference).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.InstructionId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.EndToEndId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.EndToEndId).Must(value => !IsInitiated(value) || value!.Trim().Length > 4)
            .WithMessage("The initiation prefix must be followed by the original payment identifier.");
        RuleFor(x => x.CreationDateTime).NotNull().WithMessage("Creation time is required.");
        RuleFor(x => x.AcceptanceDateTime).NotNull().WithMessage("Acceptance time is required.");
        RuleFor(x => x.AcceptanceDateTime).Custom((accepted, context) =>
        {
            if (accepted is null || context.InstanceToValidate.CreationDateTime is not { } created)
            {
                return;
            }

            var initiated = IsInitiated(context.InstanceToValidate.EndToEndId);
            if (initiated ? accepted > created : accepted < created || accepted - created > TimeSpan.FromSeconds(1))
            {
                context.AddFailure(initiated ? "Original request time cannot follow creation time." : "Acceptance must be within one second after creation.");
            }
        });
        RuleFor(x => x.Currency).ProtocolText(Pacs008Text.Currency, required: true);
        RuleFor(x => x.Currency).Must(code => policy.FindCurrency(code) is { Enabled: true })
            .WithMessage("Currency is not enabled.");
        RuleFor(x => x.Amount).Must(amount => amount is > 0).WithMessage("A positive amount is required.");
        RuleFor(x => x.Amount).Must(amount => amount is null || HasAllowedPrecision(amount.Value))
            .WithMessage("Amount permits 13 integer and 5 fractional digits.");
        RuleFor(x => x.Amount).Must((request, amount) => WithinCurrencyLimits(amount, policy.FindCurrency(request.Currency)))
            .WithMessage("Amount is outside the configured currency limits.");
        RuleFor(x => x.InstructionPriority).Must(value => value is "NORM" or "HIGH").WithMessage("Use NORM or HIGH.");
        RuleFor(x => x.CategoryPurposeCode).ProtocolText("^[A-Za-z0-9]{1,4}$");
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

    private static bool HasAllowedPrecision(decimal amount)
    {
        const decimal integerLimit = 10000000000000m;
        if (amount <= -integerLimit || amount >= integerLimit)
        {
            return false;
        }

        var formatted = amount.ToString("0.############################", CultureInfo.InvariantCulture);
        var point = formatted.IndexOf('.');
        return point < 0 || formatted.Length - point - 1 <= 5;
    }

    private static bool WithinCurrencyLimits(decimal? amount, PaymentCurrency? currency) =>
        amount is not > 0 || currency is null ||
        ((currency.Minimum is null || amount >= currency.Minimum) && (currency.Maximum is null || amount <= currency.Maximum));
}
