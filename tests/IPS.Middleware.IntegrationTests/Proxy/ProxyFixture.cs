using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Proxy;
using IPS.MiidleWear.Contracts.Proxy;

namespace IPS.Middleware.IntegrationTests.Proxy;

internal static class ProxyFixture
{
    internal const string Participant = "BAGAGE22";
    internal const string ProxyBic = "PROXGE22";
    internal const string Iban = "GE29NB0000000101904917";
    internal static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 30, 15, TimeSpan.FromHours(4));
    internal static readonly XNamespace Acmt = "urn:iso:std:iso:20022:tech:xsd:acmt.022.001.04";
    internal static readonly XNamespace Head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";

    internal static ProxySettings Settings() => new() { Enabled = true, ParticipantBic = Participant, ProxyBic = ProxyBic };

    internal static ProxyAccountHolder Holder(string type = "Individual") => new(
        "01001011111", type, "გიორგი", "ბერიძე", "დავითის", false, "GE", "GE", "LLC", "Test Company", true, "GE");

    internal static RegisterProxyRequest Register() => new(
        Holder(),
        new ProxyAccount(Iban, true, "GEL", "CACC", new DateOnly(2026, 1, 1), null),
        [new("MBNO", "+995599123456"), new("EMAL", "a@b.ge")],
        [new("auth-1", "Given", "Surname", null, null, "GE", "GE", new DateOnly(2026, 1, 1), null)],
        [new("owner-1", "Given", "Surname", "Middle", true, "GE", "GE", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1))]);

    internal static UpdateProxyRequest Update() => new(
        "01001011111", "Individual", Holder(), Iban, "GEL", null, [new("MBNO", "+995599000000")],
        [new("auth-1", "Given", "Surname", "Middle", null, null, null, new DateOnly(2026, 2, 1), null)], null);

    internal static RemoveProxyRequest Remove(bool keepAccountActive = false) => new(
        "01001011111", "Individual", Iban, "GEL", keepAccountActive, [new("MBNO", "+995599123456")], ["auth-1"], ["owner-1"]);

    internal static IProxyProtocol Protocol(ISigningCertificateSource? certificates = null) => new ProxyProtocol(
        Settings(),
        new Pacs008MessageSigner(new Pacs008SigningPolicy(false, false), new FixedTime(Now)),
        certificates ?? new NoCertificate(),
        new FixedTime(Now));

    // The operation reference the Proxy Solution is asked to answer is in the first Mod of the sent message.
    internal static string OperationId(string sentXml) => XDocument.Parse(sentXml).Descendants(Acmt + "Mod").First().Element(Acmt + "Id")!.Value;

    internal static string Accepted(string operationId) => Reply(group: "ACCP", item: ("ACCP", operationId, null, null));

    internal static string Rejected(string operationId, string? code, string? description) => Reply(group: "ACCP", item: ("RJCT", operationId, code, description));

    internal static string BulkRejected(string? code, string? description) => Reply(group: "RJCT", bulkReason: (code, description));

    private static string Reply(string group, (string Status, string OperationId, string? Code, string? Description)? item = null, (string? Code, string? Description)? bulkReason = null)
    {
        static string Reason(string? code, string? description) =>
            code is null && description is null
                ? ""
                : "<StsRsnInf>" + (code is null ? "" : $"<Rsn><Cd>{code}</Cd></Rsn>") + (description is null ? "" : $"<AddtlInf>{description}</AddtlInf>") + "</StsRsnInf>";

        var groupReason = bulkReason is { } bulk ? Reason(bulk.Code, bulk.Description) : "";
        var transaction = item is { } value
            ? $"<TxInfAndSts><OrgnlTxId>{value.OperationId}</OrgnlTxId><TxSts>{value.Status}</TxSts>{Reason(value.Code, value.Description)}</TxInfAndSts>"
            : "";
        return "<Message xmlns:hdr=\"urn:montran:message.01\"><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.002.001.13\"><FIToFIPmtStsRpt>"
            + $"<OrgnlGrpInfAndSts><GrpSts>{group}</GrpSts>{groupReason}</OrgnlGrpInfAndSts>{transaction}</FIToFIPmtStsRpt></Document></Message>";
    }

    internal static ProxyRegisterRequestDto RegisterDto() => new(
        new ProxyAccountHolderDto("01001011111", "Individual", "გიორგი", "ბერიძე", null, false, "GE", "GE", null, null, null, null),
        new ProxyAccountDto(Iban, true, "GEL", "CACC", new DateOnly(2026, 1, 1), null),
        [new ProxyIdentifierDto("MBNO", "+995599123456")],
        null,
        null);

    internal static ProxyRemoveRequestDto RemoveDto() => new("01001011111", "Individual", Iban, "GEL", false, null, null, null);

    private sealed class NoCertificate : ISigningCertificateSource
    {
        public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<X509Certificate2?>(null);
    }

    internal sealed class SingleCertificate(X509Certificate2 certificate) : ISigningCertificateSource
    {
        public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<X509Certificate2?>(certificate);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
