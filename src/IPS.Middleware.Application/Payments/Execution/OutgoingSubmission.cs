using IPS.Middleware.Application.Diagnostics;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Payments.Execution;

public sealed record OutgoingIntake(OutgoingStatus Status, bool Created, DateTimeOffset CommittedAtUtc);

public sealed record OutgoingAcceptance(OutgoingIntake? Intake, IReadOnlyList<IntakeValidationError> Errors);

public sealed record OutgoingSubmissionResult(OutgoingStatus? Status, bool TimedOut, IReadOnlyList<IntakeValidationError> Errors);

// Fresh execution scopes and service-owned admission; caller tokens only govern intake and reads.
public interface IOutgoingExecution
{
    Task<OutgoingAcceptance> AcceptAsync(IOutgoingPaymentRequest request, string json, CancellationToken token);
    bool TryStart(Guid paymentId);
    Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken token);
}

public sealed class OutgoingSubmission(
    IOutgoingExecution execution,
    OutgoingAttemptSignals attempts,
    OutgoingExecutionOptions options,
    TimeProvider time)
{
    public async Task<OutgoingSubmissionResult> SubmitAsync(IOutgoingPaymentRequest request, string json, CancellationToken token)
    {
        var accepted = await execution.AcceptAsync(request, json, token);
        if (accepted.Intake is not { } intake)
        {
            PaymentMetrics.ValidationRejected(request.GetType().Name);
            return new(null, false, accepted.Errors);
        }

        if (!intake.Created)
        {
            return new(intake.Status, false, []);
        }

        var paymentId = intake.Status.PaymentId;
        var attempt = attempts.NextAsync(paymentId);
        try
        {
            execution.TryStart(paymentId);
            return await WaitForOutcomeAsync(intake, attempt, token);
        }
        finally
        {
            attempts.Forget(paymentId);
        }
    }

    // Reads the committed status when an attempt here finishes, or else on a poll backing off from StatusPollInterval to
    // StatusPollMaxInterval, until the outcome is reportable or the HTTP wait ends.
    private async Task<OutgoingSubmissionResult> WaitForOutcomeAsync(OutgoingIntake intake, Task attempt, CancellationToken token)
    {
        var status = intake.Status;
        var deadline = intake.CommittedAtUtc + options.HttpWait;
        var remaining = deadline - time.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return new(status, true, []);
        }

        using var budget = new CancellationTokenSource(remaining, time);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token);
        var poll = options.StatusPollInterval;
        try
        {
            while (true)
            {
                await Task.WhenAny(attempt, Task.Delay(poll, time, waiting.Token));
                waiting.Token.ThrowIfCancellationRequested();
                if (attempt.IsCompleted)
                {
                    attempt = attempts.NextAsync(status.PaymentId);
                }

                OutgoingStatus? read;
                try
                {
                    read = await execution.ReadAsync(status.ClientReference, waiting.Token);
                }
                catch (Exception) when (!waiting.Token.IsCancellationRequested)
                {
                    // The payment is committed, so a failed read (a deadlock victim, a timeout) is only a missed poll: the caller
                    // must not be told to repeat the request. The next poll or the end of the HTTP wait decides.
                    PaymentMetrics.ErrorLogged(nameof(OutgoingSubmission));
                    poll = Longer(poll);
                    continue;
                }

                var observed = read ?? throw new InvalidOperationException("Durable intake has no current status.");
                if (time.GetUtcNow() >= deadline)
                {
                    return new(status, true, []);
                }

                status = observed;
                if (status.IsReportable)
                {
                    return new(status, false, []);
                }

                poll = Longer(poll);
            }
        }
        catch (Exception) when (budget.IsCancellationRequested && !token.IsCancellationRequested)
        {
            // The HTTP wait ended, possibly inside a read it cancelled: the caller gets the last committed status it saw.
            return new(status, true, []);
        }
    }

    private TimeSpan Longer(TimeSpan poll) => poll * 2 < options.StatusPollMaxInterval ? poll * 2 : options.StatusPollMaxInterval;
}
