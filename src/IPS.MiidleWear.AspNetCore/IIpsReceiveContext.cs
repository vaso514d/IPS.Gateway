using IPS.MiidleWear.Contracts.Http;

namespace IPS.MiidleWear.AspNetCore;

/// <summary>
/// What the gateway sent with the current call that the <c>IClientPaymentReceiver</c> methods do not carry. Inject it
/// into the receiver; it is scoped to the request.
/// </summary>
public interface IIpsReceiveContext
{
    /// <summary>
    /// The <see cref="IpsHttpHeaders.IdempotencyKey"/> header of the current call: the same value for every redelivery of
    /// one message (see <see cref="IpsIdempotencyKeys"/>). <c>null</c> on the payment-status query, which carries none.
    /// </summary>
    string? IdempotencyKey { get; }
}

internal sealed class IpsReceiveContext : IIpsReceiveContext
{
    public string? IdempotencyKey { get; set; }
}
