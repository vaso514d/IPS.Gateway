using FluentValidation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008.Validation;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.Middleware.Application.Payments.Recalls.Validation;

namespace IPS.Middleware.Application.Payments.Camt056.Validation;

// The source's content rules for an outgoing camt.056 (Annex D 3.2.4): the debtor agent must be our participant and the
// settlement dates may not lie in the future. Nothing is checked against the recalled payment.
internal sealed class Camt056Validator : AbstractValidator<Camt056Request>
{
    public Camt056Validator(Pacs008Policy policy, DateOnly today)
    {
        RuleFor(x => x.ClientReference).ProtocolText(Pacs008Text.HeaderText(35), required: true);
        RuleFor(x => x.Id).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.RecallId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.OriginalMessageId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.OriginalEndToEndId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.OriginalTransactionId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.OriginalCurrency).ProtocolText(Pacs008Text.Currency, required: true);
        RuleFor(x => x.OriginalCurrency).Must(code => policy.FindCurrency(code) is { Enabled: true })
            .WithMessage("Currency is not enabled.");
        RuleFor(x => x.OriginalAmount).Must(amount => amount is > 0).WithMessage("The original amount must be greater than zero.");
        RuleFor(x => x.OriginalAmount).Must(amount => amount is null || PaymentChecksums.HasAllowedPrecision(amount.Value))
            .WithMessage("Amount permits 13 integer and 5 fractional digits.");
        RuleFor(x => x.OriginalSettlementDate).NotNull().WithMessage("The original settlement date is required.");
        RuleFor(x => x.OriginalSettlementDate).Must(date => date is null || date <= today)
            .WithMessage("The original settlement date must not be in the future.");
        RuleFor(x => x.ReasonCode).ProtocolText(Pacs008Text.Text(4), required: true);
        RuleFor(x => x.OriginalTransaction).NotNull().WithMessage("The original transaction is required.");
        RuleFor(x => x.OriginalTransaction).SetValidator(new RecallOriginalValidator<RecallOriginalInput>(policy, today, RecallSender.DebtorAgent)!);
    }
}
