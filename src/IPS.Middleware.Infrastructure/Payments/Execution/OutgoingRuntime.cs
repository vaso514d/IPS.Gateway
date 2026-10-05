using System.Threading.Channels;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Payments.Execution;

public sealed class OutgoingRuntime : BackgroundService, IOutgoingExecution
{
    private readonly IServiceScopeFactory scopes;
    private readonly OutgoingExecutionOptions options;
    private readonly StatusDeliveryOptions delivery;
    private readonly TimeProvider time;
    private readonly ILogger<OutgoingRuntime> logger;
    private readonly SupervisedWork<Guid> payments;
    private readonly SupervisedWork<StatusDeliveryKey> callbacks;
    private readonly Channel<Guid> recovery;
    private readonly CancellationTokenSource admission = new();
    private readonly CancellationTokenSource execution = new();
    private readonly Lock lifecycle = new();
    private Task? stopping;
    private bool disposed;

    public OutgoingRuntime(
        IServiceScopeFactory scopes,
        OutgoingExecutionOptions options,
        StatusDeliveryOptions delivery,
        TimeProvider time,
        ILogger<OutgoingRuntime> logger)
    {
        this.scopes = scopes;
        this.options = options;
        this.delivery = delivery;
        this.time = time;
        this.logger = logger;
        payments = new(options.Concurrency, logger);
        callbacks = new(options.CallbackConcurrency, logger);
        recovery = Channel.CreateBounded<Guid>(options.ChannelCapacity);
    }

    public async Task<OutgoingAcceptance> AcceptAsync(Pacs008Request request, string json, CancellationToken token)
    {
        RequireEnabled();
        await using var scope = scopes.CreateAsyncScope();
        var accepted = await scope.ServiceProvider.GetRequiredService<Pacs008Intake>().AcceptAsync(request, json, token);
        var committedAt = time.GetUtcNow();
        if (accepted.Intake is not { } intake)
        {
            return new(null, accepted.Errors);
        }
        // A successful new intake already has committed metadata tracked locally. Duplicates use a fresh read scope,
        // because a uniqueness race may have left the intake unit of work unusable.
        var status = intake.Created
            ? OutgoingStatusProjection.Read(scope.ServiceProvider.GetRequiredService<TransactionDbContext>().Metadata(intake.Payment))
            : await ReadAsync(intake.Payment.ClientReference, token) ?? throw new InvalidOperationException("Duplicate intake has no stored outcome.");
        return new(new(status, intake.Created, committedAt), []);
    }
    public bool TryStart(Guid paymentId) => options.Enabled && !admission.IsCancellationRequested &&
        payments.TryStart(paymentId, () => ProcessAsync(paymentId));
    public async Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken token)
    {
        RequireEnabled();
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutgoingStatusRepository>().ReadAsync(reference, token);
    }
    private void RequireEnabled()
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("Outgoing execution is disabled.");
        }
    }

    private async Task ProcessAsync(Guid id)
    {
        using var budget = new CancellationTokenSource(options.AttemptBudget, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, execution.Token);
        // Recovery may fail its unit of work; never retain that scope for actual processing.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var payment = await scope.ServiceProvider.GetRequiredService<IOutgoingPaymentRepository>().FindAsync(id, stop.Token);
            if (payment?.MessageType != "pacs.008")
            {
                return;
            }

            await scope.ServiceProvider.GetRequiredService<OutgoingTransactionWork>().TryRecoverAsync(id, stop.Token);
        }
        await using var processing = scopes.CreateAsyncScope();
        await processing.ServiceProvider.GetRequiredService<Pacs008Processing>().ProcessAsync(id, stop.Token);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        using var registration = stoppingToken.Register(() =>
        {
            admission.Cancel();
            execution.Cancel();
        });
        try
        {
            await Task.WhenAll(RecoverAsync(admission.Token), DeliverAsync(admission.Token));
        }
        catch (OperationCanceledException) when (admission.IsCancellationRequested) { }
    }
    private async Task RecoverAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await RefillRecoveryAsync(stop);
                while (!stop.IsCancellationRequested && recovery.Reader.TryRead(out var id))
                {
                    TryStart(id);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Outgoing recovery discovery failed; the next sweep will retry");
            }
            await Task.Delay(options.DiscoveryInterval, time, stop);
        }
    }
    private async Task RefillRecoveryAsync(CancellationToken stop)
    {
        await using var scope = scopes.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<ITransactionWorkRepository>();
        var now = time.GetUtcNow();
        foreach (var id in await work.FindExpiredAsync(now, options.DiscoveryBatch, stop))
        {
            recovery.Writer.TryWrite(id);
        }

        foreach (var status in new[] { TransactionStatus.Received, TransactionStatus.Sending })
        {
            foreach (var id in await work.FindDueAsync(status, now, options.DiscoveryBatch, stop))
            {
                recovery.Writer.TryWrite(id);
            }
        }

    }

    private async Task DeliverAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var keys = await scope.ServiceProvider.GetRequiredService<IOutgoingStatusRepository>().FindDueAsync(time.GetUtcNow(), delivery.DiscoveryBatch, stop);
                foreach (var key in keys)
                {
                    stop.ThrowIfCancellationRequested();
                    callbacks.TryStart(key, () => DeliverCallbackAsync(key));
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Outgoing callback discovery failed; the next sweep will retry");
            }
            await Task.Delay(delivery.DiscoveryInterval, time, stop);
        }
    }

    private async Task DeliverCallbackAsync(StatusDeliveryKey key)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutgoingStatusDelivery>().DeliverAsync(key, execution.Token);
    }

    public override Task StopAsync(CancellationToken token)
    {
        lock (lifecycle)
        {
            if (disposed)
            {
                return stopping ?? Task.CompletedTask;
            }

            return stopping ??= DrainAsync(token);
        }
    }
    private async Task DrainAsync(CancellationToken token)
    {
        await admission.CancelAsync();
        var drained = Task.WhenAll(payments.StopAdmission(), callbacks.StopAdmission(), ExecuteTask ?? Task.CompletedTask);
        using var budget = new CancellationTokenSource(options.ShutdownBudget, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, token);
        try
        {
            await drained.WaitAsync(stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            await execution.CancelAsync();
        }
        await drained;
        await base.StopAsync(CancellationToken.None);
    }
    public override void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            admission.Cancel();
            execution.Cancel();
            base.Dispose();
            admission.Dispose();
            execution.Dispose();
        }
    }
}
