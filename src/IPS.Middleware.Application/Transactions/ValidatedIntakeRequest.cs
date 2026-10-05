namespace IPS.Middleware.Application.Transactions;

// Validated storage input. Message-specific business validation precedes this foundation-level envelope.
public sealed class ValidatedIntakeRequest
{
    private ValidatedIntakeRequest(string messageType, string clientReference, string requestJson)
    {
        MessageType = messageType;
        ClientReference = clientReference;
        RequestJson = requestJson;
    }

    public string MessageType { get; }
    public string ClientReference { get; }
    public string RequestJson { get; }

    public static IntakeValidationResult Validate(string? messageType, string? clientReference, string? requestJson)
    {
        var errors = new List<IntakeValidationError>();
        var type = messageType?.Trim();
        var reference = clientReference?.Trim();
        if (string.IsNullOrEmpty(type))
        {
            errors.Add(new("messageType", "Message type is required."));
        }
        else if (type.Length > 16)
        {
            errors.Add(new("messageType", "Message type must not exceed 16 characters."));
        }

        if (string.IsNullOrEmpty(reference))
        {
            errors.Add(new("clientReference", "Client reference is required."));
        }
        else if (reference.Length > 35)
        {
            errors.Add(new("clientReference", "Client reference must not exceed 35 characters."));
        }

        if (string.IsNullOrWhiteSpace(requestJson))
        {
            errors.Add(new("requestJson", "Request payload is required."));
        }

        return new(errors.Count == 0 ? new(type!, reference!, requestJson!) : null, errors.AsReadOnly());
    }
}
