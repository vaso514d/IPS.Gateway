using System.Text.RegularExpressions;
using FluentValidation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008.Validation;

namespace IPS.Middleware.Application.Payments.Pacs009.Validation;

// The source's content rules for an outgoing pacs.009. Fields the IPS v1 profile does not send (UETR, priorities,
// time window) are still checked, so a caller learns about bad values even though they never reach the wire.
internal sealed class Pacs009Validator : AbstractValidator<Pacs009Request>
{
    public Pacs009Validator(Pacs008Policy policy)
    {
        RuleFor(x => x.ClientReference).ProtocolText(Pacs008Text.HeaderText(35), required: true);
        RuleFor(x => x.Id).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.InstructionId).ProtocolText(Pacs008Text.Text(35));
        RuleFor(x => x.EndToEndId).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.TransactionId).ProtocolText(Pacs008Text.Text(35));
        RuleFor(x => x.Uetr).Must(uetr => uetr is null || uetr != Guid.Empty).WithMessage("UETR must not be an empty GUID.");
        RuleFor(x => x.InstructionPriority).Must(value => value is null or 0 or 1).WithMessage("InstructionPriority must be 0 or 1.");
        RuleFor(x => x.RtgsPriority).Must(value => value is null or 0 or 1).WithMessage("RTGSPriority must be 0 or 1.");
        RuleFor(x => x.TransactionTypeCode).ProtocolText(Pacs008Text.Text(35));
        RuleFor(x => x.RejectTime).Must((x, reject) => x.FromTime is not { } from || reject is not { } until || from < until)
            .WithMessage("FromTime must be before RejectTime.");
        RuleFor(x => x.ValueDate).NotNull().WithMessage("ValueDate is required.");
        RuleFor(x => x.Currency).ProtocolText(Pacs008Text.Currency, required: true);
        RuleFor(x => x.Currency).Must(code => policy.FindCurrency(code) is { Enabled: true })
            .WithMessage("Currency is not enabled.");
        RuleFor(x => x.Amount).Must(amount => amount is > 0).WithMessage("Amount must be greater than zero.");
        RuleFor(x => x.Amount).Must(amount => amount is null || PaymentChecksums.HasAllowedPrecision(amount.Value))
            .WithMessage("Amount permits 13 integer and 5 fractional digits.");
        RuleFor(x => x.DebtorAgent).NotNull().WithMessage("DebtorAgent is required.");
        RuleFor(x => x.DebtorAgent).SetValidator(new AgentValidator(policy)!);
        RuleFor(x => x.CreditorAgent).NotNull().WithMessage("CreditorAgent is required.");
        RuleFor(x => x.CreditorAgent).SetValidator(new AgentValidator(null)!);
        RuleFor(x => x.CategoryPurpose).SetValidator(new CodeChoiceValidator(proprietaryMaxLength: 35)!);
        RuleFor(x => x.Purpose).ProtocolText(Pacs008Text.Text(35));
        // RmtInf/Ustrd is unbounded Max140Text elements; the builder splits the text, so there is no total limit.
        RuleFor(x => x.AdditionalPurpose).ProtocolText(Pacs008Text.TextAnyLength);
        RuleFor(x => x.DebtorAccount).Must(account => string.IsNullOrWhiteSpace(account) || IsIban(account))
            .WithMessage("Account must be an IBAN with a valid checksum (pacs.009 sends accounts only as IBAN).");
        RuleFor(x => x.CreditorAccount).Must(account => string.IsNullOrWhiteSpace(account) || IsIban(account))
            .WithMessage("Account must be an IBAN with a valid checksum (pacs.009 sends accounts only as IBAN).");
    }

    // The same structure and checksum as the pacs.008 accounts, so the XML schema never rejects an accepted payment.
    private static bool IsIban(string account) =>
        account.Trim() is { Length: <= 34 } trimmed
        && Regex.IsMatch(trimmed, "^[A-Z]{2}[0-9]{2}[a-zA-Z0-9]{1,30}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
        && PaymentChecksums.ValidIban(trimmed);

    // The paying agent (policy given) must be our own participant or a configured indirect participant.
    private sealed class AgentValidator : AbstractValidator<Pacs009AgentInput>
    {
        public AgentValidator(Pacs008Policy? payer)
        {
            RuleFor(x => x.Bic).ProtocolText(Pacs008Text.Bic, required: true);
            RuleFor(x => x.ClearingSystemMemberId).ProtocolText(Pacs008Text.Text(28));
            if (payer is not null)
            {
                RuleFor(x => x.Bic).Must(bic => string.IsNullOrWhiteSpace(bic) ||
                        string.Equals(bic.Trim(), payer.ParticipantBic, StringComparison.OrdinalIgnoreCase) ||
                        payer.IndirectParticipants.Contains(bic.Trim(), StringComparer.OrdinalIgnoreCase))
                    .WithMessage("The paying agent must be our participant or a configured indirect participant.");
            }
        }
    }

    // A code is 1-4 characters (the XSD external code); a proprietary value is free text up to the given length.
    private sealed class CodeChoiceValidator : AbstractValidator<Pacs009CodeInput>
    {
        public CodeChoiceValidator(int proprietaryMaxLength)
        {
            RuleFor(x => x.Type).Must(type => type is null or 0 or 1).WithMessage("Type must be 0 (code) or 1 (proprietary).");
            RuleFor(x => x.Value).ProtocolText(Pacs008Text.Text(4), required: true).When(x => x.Type != 1);
            RuleFor(x => x.Value).ProtocolText(Pacs008Text.Text(proprietaryMaxLength), required: true).When(x => x.Type == 1);
        }
    }
}
