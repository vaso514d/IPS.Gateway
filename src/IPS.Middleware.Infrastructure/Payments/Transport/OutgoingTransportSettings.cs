using IPS.Middleware.Infrastructure.Transport;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.Middleware.Infrastructure.Payments.Transport;

public sealed class OutgoingTransportSettings
{
    public bool Enabled { get; init; }
    public string ParticipantBic { get; init; } = "";
    public HttpEndpointSettings Ips { get; init; } = new() { RequestTimeout = TimeSpan.FromSeconds(25) };
    public HttpEndpointSettings Cbs { get; init; } = new();
    public string IpsVersion { get; init; } = "1";
    public string MessagePath { get; init; } = "Message";
    public string CallbackPath { get; init; } = TransactionRestApiRoutes.ReceiveStatus;
    public CertificateSettings? SigningCertificate { get; init; }
    public CertificateSettings[] IpsSignatureTrust { get; init; } = [];

    public void Validate(bool development)
    {
        if (!Enabled)
        {
            return;
        }

        if (ParticipantBic.Length is not (8 or 11) || ParticipantBic.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            throw new InvalidOperationException("Outgoing transport requires an 8 or 11 character participant BIC.");
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

        if (IpsSignatureTrust.Length == 0)
        {
            throw new InvalidOperationException("IPS signature trust certificates are required.");
        }

        if (!development && Ips.ClientCertificate is null)
        {
            throw new InvalidOperationException("IPS mutual TLS requires a client certificate.");
        }
    }
}
