namespace IPS.Middleware.Application.Transactions;

public sealed record TransactionClaim(Guid TransactionId, Guid Token, DateTimeOffset ExpiresAtUtc);
