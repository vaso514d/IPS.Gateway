using System.Text;
using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Infrastructure.Transport;
using Polly.Timeout;

namespace IPS.Middleware.Infrastructure.Proxy;

// One POST per operation to the Proxy Solution (Annex E 3.2 to 3.4), never retried. A timeout, a connection failure or a
// non-success status is reported as no answer, so the outcome of the operation is unknown.
public sealed class ProxyClient(IHttpClientFactory clients, ProxySettings settings) : IProxyClient
{
    private static readonly Dictionary<ProxyOperation, string> Paths = new()
    {
        [ProxyOperation.Register] = "PRX/register",
        [ProxyOperation.Update] = "PRX/update",
        [ProxyOperation.Remove] = "PRX/remove"
    };

    public async Task<ProxyReply> SendAsync(ProxyOperation operation, string xml, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Paths[operation])
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
        request.Headers.Add(ProxyHeaders.Channel, settings.ParticipantBic.ToUpperInvariant());
        request.Headers.Add(ProxyHeaders.Version, settings.ProtocolVersion);
        request.Headers.Connection.Add("keep-alive");
        try
        {
            var (status, body, _) = await HttpEvidence.SendAsync(clients, ProxyHttpRegistration.Name, request, cancellationToken);
            return status is >= 200 and < 300 ? ProxyReply.Replied(body) : new ProxyReply(ProxyDelivery.Failed, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is TimeoutRejectedException or OperationCanceledException)
        {
            return new ProxyReply(ProxyDelivery.TimedOut, null);
        }
        catch (Exception)
        {
            // Connection failures, an open circuit and an unreadable body all leave the outcome unknown.
            return new ProxyReply(ProxyDelivery.Failed, null);
        }
    }
}
