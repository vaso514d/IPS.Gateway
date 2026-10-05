namespace IPS.Middleware.Application.Transactions;

public sealed class TransactionClaim
{
    [System.Text.Json.Serialization.JsonConstructor]
    public TransactionClaim(Guid transactionId, Guid token, DateTimeOffset expiresAtUtc)
    {
        TransactionId = transactionId;
        Token = token;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid TransactionId { get; init; }
    public Guid Token { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }

    public TransactionClaim(TransactionClaim original)
    {
        TransactionId = original.TransactionId;
        Token = original.Token;
        ExpiresAtUtc = original.ExpiresAtUtc;
    }
}
