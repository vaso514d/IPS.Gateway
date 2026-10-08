using System.Text.RegularExpressions;
using FluentValidation.Results;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments;

internal static class IntakeErrors
{
    internal static IReadOnlyList<IntakeValidationError> From(ValidationResult result) =>
        Array.AsReadOnly(result.Errors.Select(error => new IntakeValidationError(Path(error.PropertyName), error.ErrorMessage)).ToArray());

    // Retain the existing camel-case, unindexed error paths at this boundary.
    private static string Path(string property)
    {
        var parts = Regex.Replace(property, @"\[\d+\]", "")
            .Split('.')
            .Select(part => char.ToLowerInvariant(part[0]) + part[1..]);
        return string.Join('.', parts);
    }
}
