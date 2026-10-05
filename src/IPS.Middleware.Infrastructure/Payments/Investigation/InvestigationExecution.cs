using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Payments.Investigation;

// Recovery and processing must not retain each other's tracked or failed unit of work.
public sealed class InvestigationExecution(IServiceScopeFactory scopes)
{
    public async Task<PaymentOutcome?> RunAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        await using (var recovery = scopes.CreateAsyncScope())
        {
            await recovery.ServiceProvider.GetRequiredService<OutgoingTransactionWork>().TryRecoverAsync(paymentId, cancellationToken);
        }

        await using var processing = scopes.CreateAsyncScope();
        return await processing.ServiceProvider.GetRequiredService<OutgoingInvestigation>().ProcessAsync(paymentId, cancellationToken);
    }
}
