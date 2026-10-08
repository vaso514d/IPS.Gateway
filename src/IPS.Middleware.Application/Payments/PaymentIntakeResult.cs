using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments;

// The outcome of accepting one outgoing payment request: the stored payment, or what was wrong with the request.
public sealed record PaymentIntakeResult(TransactionIntakeResult? Intake, IReadOnlyList<IntakeValidationError> Errors);
