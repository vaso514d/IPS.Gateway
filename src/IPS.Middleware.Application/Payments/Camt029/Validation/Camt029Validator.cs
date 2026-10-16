using FluentValidation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008.Validation;
using IPS.Middleware.Application.Payments.Recalls.Validation;

namespace IPS.Middleware.Application.Payments.Camt029.Validation;

// The source's content rules for an outgoing camt.029 (Annex D 3.2.5): the creditor agent must be our participant and the
// settlement date may not lie in the future. Nothing is checked against the recall it refuses.
internal sealed class Camt029Validator : AbstractValidator<Camt029Request>
{
    public Camt029Validator(Pacs008Policy policy, DateOnly today)
    {
        RuleFor(x => x.ClientReference).ProtocolText(Pacs008Text.HeaderText(35), required: true);
        RuleFor(x => x.Id).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.CancellationStatusId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.OriginalMessageId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.OriginalEndToEndId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.OriginalTransactionId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.ReasonCode).ProtocolText(Pacs008Text.Text(4), required: true);
        RuleFor(x => x.AdditionalInformation).ProtocolText(Pacs008Text.Text(105));
        RuleFor(x => x.OriginalTransaction).NotNull().WithMessage("The original transaction is required.");
        RuleFor(x => x.OriginalTransaction).SetValidator(new OriginalValidator(policy, today)!);
    }

    // The recalled payment of a camt.056 plus its quoted amount; the answer comes from the creditor's participant.
    private sealed class OriginalValidator : RecallOriginalValidator<Camt029OriginalInput>
    {
        public OriginalValidator(Pacs008Policy policy, DateOnly today)
            : base(policy, today, RecallSender.CreditorAgent)
        {
            RuleFor(x => x.Currency).ProtocolText(Pacs008Text.Currency, required: true);
            RuleFor(x => x.Currency).Must(code => policy.FindCurrency(code) is { Enabled: true })
                .WithMessage("Currency is not enabled.");
            RuleFor(x => x.Amount).Must(amount => amount is > 0).WithMessage("The amount must be greater than zero.");
            RuleFor(x => x.Amount).Must(amount => amount is null || PaymentChecksums.HasAllowedPrecision(amount.Value))
                .WithMessage("Amount permits 13 integer and 5 fractional digits.");
        }
    }
}
