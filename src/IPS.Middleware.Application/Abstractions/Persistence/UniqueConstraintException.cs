namespace IPS.Middleware.Application.Abstractions.Persistence;

public sealed class UniqueConstraintException(string message, Exception innerException)
    : Exception(message, innerException);
