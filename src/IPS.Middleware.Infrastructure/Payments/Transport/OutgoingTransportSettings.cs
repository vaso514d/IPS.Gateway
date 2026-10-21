using IPS.Middleware.Infrastructure.Transport;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.Middleware.Infrastructure.Payments.Transport;

public sealed class OutgoingTransportSettings
{
    public bool Enabled { get; init; }
    public HttpEndpointSettings Ips { get; init; } = new() { RequestTimeout = TimeSpan.FromSeconds(25) };
    public HttpEndpointSettings Cbs { get; init; } = new();
    public string IpsVersion { get; init; } = "1";
    public string MessagePath { get; init; } = "Message";
    public string CallbackPath { get; init; } = TransactionRestApiRoutes.ReceiveStatus;
    public CertificateSettings? SigningCertificate { get; init; }
    // Optional (decision 012d): when IPS certificates are configured every IPS signature is verified with them;
    // when none are, signatures are not checked.
    public CertificateSettings[] IpsSignatureTrust { get; init; } = [];

    public void Validate(bool development)
    {
        Ips.RequireNoCoreAuthentication("Payments:Outgoing:Transport:Ips");
        Cbs.ValidateCoreAuthentication();
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(IpsVersion) || IpsVersion.Any(c => c < 33 || c > 126))
        {
            throw new InvalidOperationException("IPS version must be a nonempty ASCII header value.");
        }

        Ips.Validate(development);
        Cbs.Validate(development);
        foreach (var path in new[] { MessagePath, CallbackPath })
        {
            TransportPath.Validate(path);
        }

        Ips.RequireClientCertificate(development, "IPS");
    }
}
