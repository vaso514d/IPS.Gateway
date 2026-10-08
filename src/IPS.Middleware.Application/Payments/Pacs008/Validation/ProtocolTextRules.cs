using System.Text.RegularExpressions;
using FluentValidation;

namespace IPS.Middleware.Application.Payments.Pacs008.Validation;

internal static class ProtocolTextRules
{
    internal static IRuleBuilderOptionsConditions<T, string?> ProtocolText<T>(
        this IRuleBuilder<T, string?> rule, string pattern, bool required = false) =>
        rule.Custom((value, context) =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                {
                    context.AddFailure("A value is required.");
                }
            }
            else if (!Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                context.AddFailure("The value has an invalid format or length.");
            }
        });

    internal static void Iban<T>(this IRuleBuilder<T, string?> rule)
    {
        rule.ProtocolText("^[A-Z]{2}[0-9]{2}[a-zA-Z0-9]{1,30}$", required: true);
        rule.Must(value => string.IsNullOrWhiteSpace(value) || PaymentChecksums.ValidIban(value))
            .WithMessage("IBAN checksum is invalid.");
    }
}
