using System.Xml.Linq;
using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Proxy;

// Builds, validates and signs the acmt.022 and reads the pacs.002 answer. The same ECDSA signing as the IPS messages is used.
// Without a configured certificate the message is sent unsigned, as in the source (the Proxy Solution rejects it if it needs one).
public sealed class ProxyProtocol(
    ProxySettings settings,
    Pacs008MessageSigner signer,
    ISigningCertificateSource certificates,
    TimeProvider timeProvider) : IProxyProtocol
{
    public Task<string> PrepareRegisterAsync(RegisterProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) =>
        SignAsync(Acmt022Message.BuildRegister(request, ids, settings, timeProvider.GetUtcNow()), cancellationToken);

    public Task<string> PrepareUpdateAsync(UpdateProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) =>
        SignAsync(Acmt022Message.BuildUpdate(request, ids, settings, timeProvider.GetUtcNow()), cancellationToken);

    public Task<string> PrepareRemoveAsync(RemoveProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) =>
        SignAsync(Acmt022Message.BuildRemove(request, ids, settings, timeProvider.GetUtcNow()), cancellationToken);

    public ProxyOutcome ReadReply(string xml, string operationId) => ProxyReplyReader.Read(xml, operationId);

    private async Task<string> SignAsync(XElement message, CancellationToken cancellationToken)
    {
        var xml = message.ToString(SaveOptions.DisableFormatting);
        var certificate = await certificates.GetCurrentAsync(cancellationToken);
        if (certificate is null)
        {
            ProxySchema.Validate(xml);
            return xml;
        }

        return signer.PrepareAcmt022(xml, certificate).Xml;
    }
}
