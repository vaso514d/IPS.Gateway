using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound;

public sealed class IncomingPaymentRegistration(IServiceScopeFactory scopes)
{
    /// <summary>A receipt that lost a concurrent registration race reuses the committed winner only when the contents match.</summary>
    public Task<IncomingRegistration> RegisterAsync(InboundClaim claim, IncomingPacs008 incoming, CancellationToken cancellationToken) =>
        scopes.RetryAsync<IncomingPaymentIntake, IncomingRegistration>(
            intake => intake.RegisterAsync(claim, incoming, cancellationToken), cancellationToken);
}
