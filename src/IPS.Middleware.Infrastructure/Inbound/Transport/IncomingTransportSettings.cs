using IPS.Middleware.Infrastructure.Transport;
using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt055;
using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;
using IPS.MiidleWear.Contracts.Pain001;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public sealed class IncomingTransportSettings
{
    public bool Enabled { get; init; }
    public HttpEndpointSettings Ips { get; init; } = new();
    public HttpEndpointSettings Cbs { get; init; } = new();
    public string IpsVersion { get; init; } = "1";
    public string MessagePath { get; init; } = "Message";
    public string AckPath { get; init; } = "MessageAck";
    // An incoming pacs.008 (specification 022): delivered to SubmissionPath, queried at IncomingPaymentStatusPath, whose
    // {transactionId} is replaced by the payment's transactionId.
    public const string TransactionIdPlaceholder = "{transactionId}";
    public string SubmissionPath { get; init; } = IncomingPaymentRestApiRoutes.Receive;
    public string IncomingPaymentStatusPath { get; init; } = IncomingPaymentRestApiRoutes.Status;
    public string Pain001SubmissionPath { get; init; } = Pain001RestApiRoutes.Receive;
    public string Pacs004SubmissionPath { get; init; } = Pacs004RestApiRoutes.Receive;
    // An incoming pacs.009 (specification 023): delivered to Pacs009SubmissionPath, queried at FiTransferStatusPath.
    public string Pacs009SubmissionPath { get; init; } = IncomingFiTransferRestApiRoutes.Receive;
    public string FiTransferStatusPath { get; init; } = IncomingFiTransferRestApiRoutes.Status;
    public string Camt056SubmissionPath { get; init; } = Camt056RestApiRoutes.Receive;
    public string Camt055SubmissionPath { get; init; } = Camt055RestApiRoutes.Receive;
    public string Camt029SubmissionPath { get; init; } = Camt029RestApiRoutes.Receive;
    public string StatusPath { get; init; } = TransactionRestApiRoutes.PaymentStatus;
    public string ReversalPath { get; init; } = TransactionRestApiRoutes.ReceiveStatus;
    public TimeSpan ReceiveTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public CertificateSettings? SigningCertificate { get; init; }
    // Optional (decision 012d): when IPS certificates are configured every IPS signature is verified with them;
    // when none are, signatures are not checked.
    public CertificateSettings[] IpsSignatureTrust { get; init; } = [];

    public void Validate(bool development)
    {
        Ips.RequireNoCoreAuthentication("Payments:Incoming:Transport:Ips");
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
        string[] paths =
        [
            MessagePath, AckPath, SubmissionPath, Pacs009SubmissionPath, Pacs004SubmissionPath, Pain001SubmissionPath,
            Camt056SubmissionPath, Camt055SubmissionPath, Camt029SubmissionPath, StatusPath, ReversalPath, IncomingPaymentStatusPath, FiTransferStatusPath
        ];
        foreach (var path in paths)
        {
            TransportPath.Validate(path);
        }

        if (!IncomingPaymentStatusPath.Contains(TransactionIdPlaceholder, StringComparison.Ordinal)
            || !FiTransferStatusPath.Contains(TransactionIdPlaceholder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"IncomingPaymentStatusPath and FiTransferStatusPath must contain {TransactionIdPlaceholder}.");
        }

        if (ReceiveTimeout <= Ips.ConnectTimeout || ReceiveTimeout > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("Receive timeout must exceed the IPS connection timeout and be at most one day.");
        }

        if (Ips.ConnectionLimit < 2 || Cbs.ConnectionLimit < 3)
        {
            throw new InvalidOperationException("IPS requires a receive reservation; CBS requires two follow-up reservations.");
        }

        Ips.RequireClientCertificate(development, "IPS");
    }
}
