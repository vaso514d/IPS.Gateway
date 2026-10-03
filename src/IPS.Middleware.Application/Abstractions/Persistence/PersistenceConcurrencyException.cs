namespace IPS.Middleware.Application.Abstractions.Persistence;

public sealed class PersistenceConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);
