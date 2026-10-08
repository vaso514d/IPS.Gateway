using System.Diagnostics;
using IPS.Middleware.Application.Diagnostics;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Proxy.Validation;
using IPS.Middleware.Application.Transactions;

namespace IPS.Middleware.Application.Proxy;

public enum ProxyOperation
{
    Register,
    Update,
    Remove
}

// The operation reference and the bulk message reference of one call. A fresh pair per call: the Proxy Solution
// deduplicates references for 24 hours (Annex E 2.2), so a reused one would be rejected as a duplicate.
public sealed record ProxyIds(string OperationId, string BulkMessageId)
{
    public static ProxyIds New() => new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
}

public sealed record ProxyOutcome(bool Accepted, string? ErrorCode, string? Description)
{
    public static ProxyOutcome Accept() => new(true, null, null);

    public static ProxyOutcome Reject(string? errorCode, string? description) => new(false, errorCode, description);
}

public enum ProxyDelivery
{
    Replied,
    TimedOut,
    Failed
}

// The Proxy Solution's answer, or why there is none. Without an answer the outcome of the operation is unknown.
public sealed record ProxyReply(ProxyDelivery Delivery, string? Xml)
{
    public static ProxyReply Replied(string xml) => new(ProxyDelivery.Replied, xml);
}

public sealed record ProxyManagementResult(ProxyOutcome? Outcome, ProxyDelivery Delivery, IReadOnlyList<IntakeValidationError> Errors)
{
    public static ProxyManagementResult Invalid(IReadOnlyList<IntakeValidationError> errors) => new(null, ProxyDelivery.Replied, errors);

    public static ProxyManagementResult Unanswered(ProxyDelivery delivery) => new(null, delivery, []);
}

// Builds and signs the acmt.022 and reads the pacs.002 answer (Infrastructure).
public interface IProxyProtocol
{
    Task<string> PrepareRegisterAsync(RegisterProxyRequest request, ProxyIds ids, CancellationToken cancellationToken);

    Task<string> PrepareUpdateAsync(UpdateProxyRequest request, ProxyIds ids, CancellationToken cancellationToken);

    Task<string> PrepareRemoveAsync(RemoveProxyRequest request, ProxyIds ids, CancellationToken cancellationToken);

    ProxyOutcome ReadReply(string xml, string operationId);
}

public interface IProxyClient
{
    // One attempt; a timeout or a failure is reported, never retried.
    Task<ProxyReply> SendAsync(ProxyOperation operation, string xml, CancellationToken cancellationToken);
}

// The Proxy management operations are stateless: nothing is stored and nothing is retried, as in the source. The caller
// learns the Proxy Solution's accept or reject, or that there was no answer.
public sealed class ProxyManagement(IProxyProtocol protocol, IProxyClient client)
{
    public async Task<ProxyManagementResult> RegisterAsync(RegisterProxyRequest request, CancellationToken cancellationToken)
    {
        var validation = new RegisterProxyValidator().Validate(request);
        if (!validation.IsValid)
        {
            return Invalid(ProxyOperation.Register, validation);
        }

        var ids = ProxyIds.New();
        var xml = await protocol.PrepareRegisterAsync(request, ids, cancellationToken);
        return await ExchangeAsync(ProxyOperation.Register, xml, ids, cancellationToken);
    }

    public async Task<ProxyManagementResult> UpdateAsync(UpdateProxyRequest request, CancellationToken cancellationToken)
    {
        var validation = new UpdateProxyValidator().Validate(request);
        if (!validation.IsValid)
        {
            return Invalid(ProxyOperation.Update, validation);
        }

        var ids = ProxyIds.New();
        var xml = await protocol.PrepareUpdateAsync(request, ids, cancellationToken);
        return await ExchangeAsync(ProxyOperation.Update, xml, ids, cancellationToken);
    }

    public async Task<ProxyManagementResult> RemoveAsync(RemoveProxyRequest request, CancellationToken cancellationToken)
    {
        var validation = new RemoveProxyValidator().Validate(request);
        if (!validation.IsValid)
        {
            return Invalid(ProxyOperation.Remove, validation);
        }

        var ids = ProxyIds.New();
        var xml = await protocol.PrepareRemoveAsync(request, ids, cancellationToken);
        return await ExchangeAsync(ProxyOperation.Remove, xml, ids, cancellationToken);
    }

    private async Task<ProxyManagementResult> ExchangeAsync(ProxyOperation operation, string xml, ProxyIds ids, CancellationToken cancellationToken)
    {
        var reply = await client.SendAsync(operation, xml, cancellationToken);
        if (reply.Delivery != ProxyDelivery.Replied)
        {
            PaymentMetrics.ProxyCalled(OperationName(operation), reply.Delivery == ProxyDelivery.TimedOut ? "timed_out" : "failed");
            return ProxyManagementResult.Unanswered(reply.Delivery);
        }

        var outcome = protocol.ReadReply(reply.Xml!, ids.OperationId);
        PaymentMetrics.ProxyCalled(OperationName(operation), outcome.Accepted ? "accepted" : "rejected");
        return new ProxyManagementResult(outcome, ProxyDelivery.Replied, []);
    }

    private static ProxyManagementResult Invalid(ProxyOperation operation, FluentValidation.Results.ValidationResult validation)
    {
        PaymentMetrics.ValidationRejected("proxy." + OperationName(operation));
        return ProxyManagementResult.Invalid(IntakeErrors.From(validation));
    }

    private static string OperationName(ProxyOperation operation) => operation.ToString().ToLowerInvariant();
}
