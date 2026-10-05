namespace IPS.Middleware.Application.Transactions;

public sealed record IntakeValidationResult(ValidatedIntakeRequest? Request, IReadOnlyList<IntakeValidationError> Errors);

public sealed record IntakeValidationError(string Field, string Message);
