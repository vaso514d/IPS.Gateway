using System.Threading.Channels;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Hosting;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Payments.Execution;

// Runs outgoing payments, investigations, resends and callbacks in fresh scopes with service-owned admission;
// SQL claims stay the authority.
public sealed class OutgoingRuntime(
    IServiceScopeFactory scopes,
    OutgoingExecutionOptions options,
    InvestigationOptions investigation,
    StatusDeliveryOptions delivery,
    TimeProvider time,
    ILogger<OutgoingRuntime> logger)
    : SupervisedBackgroundService(options.Enabled, options.ShutdownBudget, time, logger), IOutgoingExecution
{
    private readonly SupervisedWork<Guid> _payments = new(options.Concurrency, logger);
    private readonly SupervisedWork<StatusDeliveryKey> _callbacks = new(options.CallbackConcurrency, logger);
    private readonly Channel<Guid> _recovery = Channel.CreateBounded<Guid>(options.ChannelCapacity);

    public async Task<OutgoingAcceptance> AcceptAsync(IOutgoingPaymentRequest request, string json, CancellationToken token)
    {
        RequireEnabled();
        await using var scope = scopes.CreateAsyncScope();
        var accepted = await AcceptRequestAsync(scope.ServiceProvider, request, json, token);
        var committedAt = Time.GetUtcNow();
        if (accepted.Intake is not { } intake)
        {
            return new OutgoingAcceptance(null, accepted.Errors);
        }

        // A successful new intake already has committed metadata tracked locally. Duplicates use a fresh read scope,
        // because a uniqueness race may have left the intake unit of work unusable.
        var status = intake.Created
            ? OutgoingStatusProjection.Read(scope.ServiceProvider.GetRequiredService<TransactionDbContext>().Metadata(intake.Payment))
            : await ReadAsync(intake.Payment.ClientReference, token)
                ?? throw new InvalidOperationException("Duplicate intake has no stored outcome.");
        return new OutgoingAcceptance(new OutgoingIntake(status, intake.Created, committedAt), []);
    }

    private static Task<PaymentIntakeResult> AcceptRequestAsync(IServiceProvider services, IOutgoingPaymentRequest request, string json, CancellationToken token) =>
        request switch
        {
            Pacs008Request pacs008 => services.GetRequiredService<Pacs008Intake>().AcceptAsync(pacs008, json, token),
            Pacs009Request pacs009 => services.GetRequiredService<Pacs009Intake>().AcceptAsync(pacs009, json, token),
            Pacs004Request pacs004 => services.GetRequiredService<Pacs004Intake>().AcceptAsync(pacs004, json, token),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.GetType().Name, "Unsupported outgoing payment request.")
        };

    public bool TryStart(Guid paymentId) =>
        options.Enabled && IsAdmitting && _payments.TryStart(paymentId, () => ProcessAsync(paymentId));

    public async Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken token)
    {
        RequireEnabled();
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutgoingStatusRepository>().ReadAsync(reference, token);
    }

    protected override Task RunAsync(CancellationToken stop, CancellationToken work) => Task.WhenAll(
        RunSweepsAsync("Outgoing recovery discovery", StartDuePaymentsAsync, options.DiscoveryInterval, stop),
        RunSweepsAsync("Outgoing investigation and resend discovery", StartDueInvestigationsAndResendsAsync, investigation.DiscoveryInterval, stop),
        RunSweepsAsync("Outgoing callback discovery", StartDueCallbacksAsync, delivery.DiscoveryInterval, stop));

    protected override Task StopAdmissionAsync() => Task.WhenAll(_payments.StopAdmission(), _callbacks.StopAdmission());

    private async Task StartDuePaymentsAsync(CancellationToken stop)
    {
        await RefillRecoveryAsync(stop);
        while (!stop.IsCancellationRequested && _recovery.Reader.TryRead(out var paymentId))
        {
            TryStart(paymentId);
        }
    }

    private async Task RefillRecoveryAsync(CancellationToken stop)
    {
        await using var scope = scopes.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<ITransactionWorkRepository>();
        var now = Time.GetUtcNow();
        Enqueue(await work.FindExpiredAsync(now, options.DiscoveryBatch, stop));
        Enqueue(await work.FindDueAsync(TransactionStatus.Received, now, options.DiscoveryBatch, stop));
        Enqueue(await work.FindDueAsync(TransactionStatus.Sending, now, options.DiscoveryBatch, stop));
    }

    private void Enqueue(IEnumerable<Guid> paymentIds)
    {
        foreach (var paymentId in paymentIds)
        {
            _recovery.Writer.TryWrite(paymentId);
        }
    }

    private async Task StartDueInvestigationsAndResendsAsync(CancellationToken stop)
    {
        await using var scope = scopes.CreateAsyncScope();
        var now = Time.GetUtcNow();
        var resends = await scope.ServiceProvider.GetRequiredService<ITransactionWorkRepository>()
            .FindDueAsync(TransactionStatus.Resending, now, investigation.DiscoveryBatch, stop);
        var investigations = await scope.ServiceProvider.GetRequiredService<IInvestigationRepository>()
            .FindDueAsync(now, investigation, stop);
        var duplicates = await scope.ServiceProvider.GetRequiredService<IResendRepository>()
            .FindDueAsync(now, investigation, stop);
        foreach (var paymentId in resends.Concat(investigations).Concat(duplicates))
        {
            stop.ThrowIfCancellationRequested();
            TryStart(paymentId);
        }
    }

    private async Task StartDueCallbacksAsync(CancellationToken stop)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutgoingStatusRepository>();
        var keys = await repository.FindDueAsync(Time.GetUtcNow(), delivery.DiscoveryBatch, stop);
        foreach (var key in keys)
        {
            stop.ThrowIfCancellationRequested();
            _callbacks.TryStart(key, () => DeliverCallbackAsync(key));
        }
    }

    private async Task ProcessAsync(Guid paymentId)
    {
        using var budget = new CancellationTokenSource(options.AttemptBudget, Time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, ExecutionToken);
        if (await RecoverAsync(paymentId, stop.Token) is not { } payment)
        {
            return;
        }

        // A stale status is harmless: each workflow loads the payment again and acts only on the states it owns.
        // Investigations and resends apply their own attempt budget.
        await using var processing = scopes.CreateAsyncScope();
        var services = processing.ServiceProvider;
        var hasInvestigation = PaymentMessageTypes.HasInvestigation(payment.MessageType);
        switch (payment.Status)
        {
            case TransactionStatus.Received or TransactionStatus.Sending:
                await services.GetRequiredService<OutgoingPaymentProcessing>().ProcessAsync(paymentId, stop.Token);
                break;
            case TransactionStatus.Uncertain or TransactionStatus.Investigating when hasInvestigation:
                await services.GetRequiredService<OutgoingInvestigation>().ProcessAsync(paymentId, ExecutionToken);
                break;
            case TransactionStatus.Resending when hasInvestigation:
                await services.GetRequiredService<OutgoingResend>().ProcessAsync(paymentId, ExecutionToken);
                break;
            case TransactionStatus.Uncertain or TransactionStatus.Resending:
                await services.GetRequiredService<OutgoingDuplicateResend>().ProcessAsync(paymentId, ExecutionToken);
                break;
        }
    }

    // Recovery may fail its unit of work; never retain that scope for actual processing.
    private async Task<(string MessageType, TransactionStatus Status)?> RecoverAsync(Guid paymentId, CancellationToken token)
    {
        await using var recovery = scopes.CreateAsyncScope();
        var payment = await recovery.ServiceProvider.GetRequiredService<IOutgoingPaymentRepository>().FindAsync(paymentId, token);
        if (payment is null || !PaymentMessageTypes.IsOutgoing(payment.MessageType))
        {
            return null;
        }

        await recovery.ServiceProvider.GetRequiredService<OutgoingTransactionWork>().TryRecoverAsync(paymentId, token);
        return (payment.MessageType, payment.CurrentStatus);
    }
    private async Task DeliverCallbackAsync(StatusDeliveryKey key)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutgoingStatusDelivery>().DeliverAsync(key, ExecutionToken);
    }

    private void RequireEnabled()
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("Outgoing execution is disabled.");
        }
    }
}
