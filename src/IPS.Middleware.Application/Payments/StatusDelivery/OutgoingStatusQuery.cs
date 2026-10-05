using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.StatusDelivery;

public static class OutgoingStatusQuery
{
    public static IReadOnlyList<IntakeValidationError> Validate(string? messageType, string? reference)
    {
        var errors = new List<IntakeValidationError>();
        if (messageType is null) errors.Add(new("messageKind", "messageKind must be Pacs008, Pacs009, Pacs004, Camt056, Camt029 or Pain002."));
        if (string.IsNullOrWhiteSpace(reference)) errors.Add(new("clientReference", "clientReference is required."));
        else if (reference.Trim().Length > 35) errors.Add(new("clientReference", "clientReference must be at most 35 characters."));
        return errors;
    }
}
