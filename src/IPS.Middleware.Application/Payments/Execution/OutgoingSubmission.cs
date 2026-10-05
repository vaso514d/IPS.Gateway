using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Execution;

public sealed class OutgoingIntake
{
    public OutgoingIntake(OutgoingStatus status, bool created, DateTimeOffset committedAtUtc)
    {
        Status = status;
        Created = created;
        CommittedAtUtc = committedAtUtc;
    }

    public OutgoingStatus Status { get; init; }
    public bool Created { get; init; }
    public DateTimeOffset CommittedAtUtc { get; init; }
}

public sealed class OutgoingAcceptance
{
    public OutgoingAcceptance(OutgoingIntake? intake, IReadOnlyList<IntakeValidationError> errors)
    {
        Intake = intake;
        Errors = errors;
    }

    public OutgoingIntake? Intake { get; init; }
    public IReadOnlyList<IntakeValidationError> Errors { get; init; }
}

public sealed class OutgoingSubmissionResult
{
    public OutgoingSubmissionResult(OutgoingStatus? status, bool timedOut, IReadOnlyList<IntakeValidationError> errors)
    {
        Status = status;
        TimedOut = timedOut;
        Errors = errors;
    }

    public OutgoingStatus? Status { get; init; }
    public bool TimedOut { get; init; }
    public IReadOnlyList<IntakeValidationError> Errors { get; init; }
}

/// <summary>Fresh execution scopes and service-owned admission; caller tokens only govern intake and reads.</summary>
public interface IOutgoingExecution
{
    Task<OutgoingAcceptance> AcceptAsync(Pacs008Request request, string json, CancellationToken token);
    bool TryStart(Guid paymentId);
    Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken token);
}

public sealed class OutgoingSubmission(IOutgoingExecution execution, OutgoingExecutionOptions options, TimeProvider time)
{
    public async Task<OutgoingSubmissionResult> SubmitAsync(Pacs008Request request, string json, CancellationToken token)
    {
        var accepted = await execution.AcceptAsync(request, json, token);
        if (accepted.Intake is not { } intake)
        {
            return new(null, false, accepted.Errors);
        }

        var status = intake.Status;
        if (!intake.Created)
        {
            return new(status, false, []);
        }

        execution.TryStart(status.PaymentId);
        var deadline = intake.CommittedAtUtc + options.HttpWait;
        var remaining = deadline - time.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return new(status, true, []);
        }

        using var budget = new CancellationTokenSource(remaining, time);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token);
        try
        {
            while (true)
            {
                var observed = await execution.ReadAsync(intake.Status.ClientReference, waiting.Token)
                    ?? throw new InvalidOperationException("Durable intake has no current status.");
                if (time.GetUtcNow() >= deadline)
                {
                    return new(status, true, []);
                }

                status = observed;
                if (status.IsReportable)
                {
                    return new(status, false, []);
                }

                await Task.Delay(options.StatusPollInterval, time, waiting.Token);
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !token.IsCancellationRequested)
        {
            return new(status, true, []);
        }
    }
}
