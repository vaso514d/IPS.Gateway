using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Execution;

public sealed record OutgoingIntake(OutgoingStatus Status, bool Created, DateTimeOffset CommittedAtUtc);

public sealed record OutgoingAcceptance(OutgoingIntake? Intake, IReadOnlyList<IntakeValidationError> Errors);

public sealed record OutgoingSubmissionResult(OutgoingStatus? Status, bool TimedOut, IReadOnlyList<IntakeValidationError> Errors);

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
