namespace IPS.Middleware.Application.Transactions;

public sealed class IntakeValidationResult
{
    public IntakeValidationResult(ValidatedIntakeRequest? request, IReadOnlyList<IntakeValidationError> errors)
    {
        Request = request;
        Errors = errors;
    }

    public ValidatedIntakeRequest? Request { get; init; }
    public IReadOnlyList<IntakeValidationError> Errors { get; init; }
}

public sealed class IntakeValidationError
{
    public IntakeValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    public string Field { get; init; }
    public string Message { get; init; }
}
