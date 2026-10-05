using IPS.Middleware.Infrastructure.Transport;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public sealed class IncomingTransportSettings
{
    public bool Enabled { get; init; }
    public string ParticipantBic { get; init; } = "";
    public HttpEndpointSettings Ips { get; init; } = new();
    public HttpEndpointSettings Cbs { get; init; } = new();
    public string IpsVersion { get; init; } = "1";
    public string MessagePath { get; init; } = "Message";
    public string SubmissionPath { get; init; } = Pacs008RestApiRoutes.Receive;
    public string StatusPath { get; init; } = TransactionRestApiRoutes.PaymentStatus;
    public string ReversalPath { get; init; } = TransactionRestApiRoutes.ReceiveStatus;
    public TimeSpan ReceiveTimeout { get; init; } = TimeSpan.FromSeconds(10);
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
            throw new InvalidOperationException("Incoming transport requires an 8 or 11 character participant BIC.");
        }

        if (string.IsNullOrWhiteSpace(IpsVersion) || IpsVersion.Any(c => c < 33 || c > 126))
        {
            throw new InvalidOperationException("IPS version must be a nonempty ASCII header value.");
        }

        Ips.Validate(development);
        Cbs.Validate(development);
        foreach (var path in new[] { MessagePath, SubmissionPath, StatusPath, ReversalPath })
        {
            TransportPath.Validate(path);
        }

        if (ReceiveTimeout <= Ips.ConnectTimeout || ReceiveTimeout > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("Receive timeout must exceed the IPS connection timeout and be at most one day.");
        }

        if (Ips.ConnectionLimit < 2 || Cbs.ConnectionLimit < 3)
        {
            throw new InvalidOperationException("IPS requires a receive reservation; CBS requires two follow-up reservations.");
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
