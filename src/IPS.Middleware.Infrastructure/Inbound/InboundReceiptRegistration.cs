using IPS.Middleware.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound;

public sealed class InboundReceiptRegistration(IServiceScopeFactory scopes, InboundProcessingChannel channel)
{
    public async Task<InboundRegistration> RegisterAsync(InboundReceipt receipt, CancellationToken cancellationToken)
    {
        // A concurrent delivery of the same sequence won the insert or update; the next attempt sees it as a duplicate.
        var result = await scopes.RetryAsync<InboundReceiptIntake, InboundRegistration>(
            intake => intake.RegisterAsync(receipt, cancellationToken), cancellationToken);
        // Notify only after a new pending receipt commits; anything missed is rediscovered from SQL.
        if (result is { Created: true, Status: InboundProcessingStatus.Pending } && !cancellationToken.IsCancellationRequested)
            channel.TryNotify(result.JournalId);
        return result;
    }
}
