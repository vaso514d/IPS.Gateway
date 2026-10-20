using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using IPS.MiidleWear.Security;
using Xunit;

namespace IPS.Middleware.Tests.Security;

// Specification 021: the core system signs Base64(SHA-256(body)) and the gateway checks it against trusted certificates.
public sealed class ContentSignatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_digest_is_the_base64_of_the_sha256_of_the_exact_bytes()
    {
        var digest = IpsContentSignature.Digest("{}"u8);

        Assert.Equal("RBNvo1WzZ4oRRq0W9+hknpT7T8If536DEMBg9hyq/4o=", digest);
        Assert.Equal(44, digest.Length);
        Assert.Equal(32, Convert.FromBase64String(digest).Length);
        Assert.Equal("47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=", IpsContentSignature.Digest([]));
    }

    [Theory]
    [InlineData(IpsSignatureAlgorithm.RS256)]
    [InlineData(IpsSignatureAlgorithm.PS256)]
    [InlineData(IpsSignatureAlgorithm.ES256)]
    public void Each_algorithm_signs_and_verifies_and_a_changed_body_fails(IpsSignatureAlgorithm algorithm)
    {
        using var certificate = Certificate(algorithm);
        var verifier = Verifier(certificate);
        var body = Encoding.UTF8.GetBytes("""{"clientReference":"CBS-1","amount":10}""");

        var signed = Sign(body, certificate, algorithm);

        Assert.Equal(IpsSignatureCheck.Valid, verifier.Verify(signed, body));
        Assert.Equal(IpsSignatureCheck.DigestMismatch, verifier.Verify(signed, Encoding.UTF8.GetBytes("""{"clientReference":"CBS-1","amount":11}""")));
        var changedDigest = signed with { ContentSha256 = IpsContentSignature.Digest(Encoding.UTF8.GetBytes("other")) };
        Assert.Equal(IpsSignatureCheck.InvalidSignature, verifier.Verify(changedDigest, Encoding.UTF8.GetBytes("other")));
    }

    [Fact]
    public void Every_other_defect_is_refused_with_its_reason()
    {
        using var certificate = Certificate(IpsSignatureAlgorithm.RS256);
        using var other = Certificate(IpsSignatureAlgorithm.RS256);
        var body = "{}"u8.ToArray();
        var signed = Sign(body, certificate, IpsSignatureAlgorithm.RS256);
        var verifier = Verifier(certificate);

        Assert.Equal(IpsSignatureCheck.MissingHeader, verifier.Verify(signed with { Signature = null }, body));
        Assert.Equal(IpsSignatureCheck.AlgorithmNotAllowed, verifier.Verify(signed with { SignatureAlgorithm = "HS256" }, body));
        Assert.Equal(IpsSignatureCheck.AlgorithmNotAllowed,
            new IpsSignatureVerifier([certificate], [IpsSignatureAlgorithm.ES256], new FixedTime(Now)).Verify(signed, body));
        Assert.Equal(IpsSignatureCheck.UnknownCertificate, verifier.Verify(signed with { Certificate = other.Thumbprint }, body));
        Assert.Equal(IpsSignatureCheck.InvalidSignature, verifier.Verify(Sign(body, other, IpsSignatureAlgorithm.RS256) with { Certificate = certificate.Thumbprint }, body));
        Assert.Equal(IpsSignatureCheck.InvalidSignature, verifier.Verify(signed with { Signature = "not base64" }, body));
        Assert.Equal(IpsSignatureCheck.InvalidSignature, verifier.Verify(signed with { SignatureAlgorithm = "PS256" }, body));
        var expired = new IpsSignatureVerifier([certificate], [IpsSignatureAlgorithm.RS256], new FixedTime(Now.AddYears(2)));
        Assert.Equal(IpsSignatureCheck.CertificateNotValidNow, expired.Verify(signed, body));
    }

    [Fact]
    public void The_thumbprint_is_matched_without_regard_to_case_and_two_certificates_can_be_trusted()
    {
        using var current = Certificate(IpsSignatureAlgorithm.RS256);
        using var next = Certificate(IpsSignatureAlgorithm.ES256);
        var verifier = new IpsSignatureVerifier([current, next], Enum.GetValues<IpsSignatureAlgorithm>(), new FixedTime(Now));
        var body = "{}"u8.ToArray();

        Assert.Equal(IpsSignatureCheck.Valid, verifier.Verify(Sign(body, current, IpsSignatureAlgorithm.RS256) with { Certificate = current.Thumbprint.ToLowerInvariant() }, body));
        Assert.Equal(IpsSignatureCheck.Valid, verifier.Verify(Sign(body, next, IpsSignatureAlgorithm.ES256), body));
    }

    [Fact]
    public async Task The_handler_sends_exactly_the_bytes_it_signed()
    {
        using var certificate = Certificate(IpsSignatureAlgorithm.ES256);
        var server = new Capture();
        using var client = new HttpClient(new IpsSigningHandler(certificate, IpsSignatureAlgorithm.ES256) { InnerHandler = server })
        {
            BaseAddress = new Uri("http://gateway/")
        };

        await client.PostAsJsonAsync("api/v1/ips/pacs008/send", new { clientReference = "CBS-1", amount = 10.5m });
        await client.GetAsync("api/v1/ips/transactions/status?messageKind=Pacs008&clientReference=CBS-1");

        Assert.Equal(2, server.Requests.Count);
        var verifier = Verifier(certificate);
        Assert.All(server.Requests, request => Assert.Equal(IpsSignatureCheck.Valid, verifier.Verify(request.Signed, request.Body)));
        Assert.Equal("application/json; charset=utf-8", server.Requests[0].ContentType);
        Assert.Equal(certificate.Thumbprint, server.Requests[0].Signed.Certificate);
        Assert.Equal("ES256", server.Requests[0].Signed.SignatureAlgorithm);
    }

    [Fact]
    public async Task The_token_handler_sends_a_bearer_token_and_nothing_without_one()
    {
        var server = new Capture();
        using (var client = new HttpClient(new IpsApiTokenHandler("secret-1") { InnerHandler = server }))
        {
            await client.GetAsync("http://core/api/ips/payments/status");
        }

        var open = new Capture();
        using (var client = new HttpClient(new IpsApiTokenHandler(null) { InnerHandler = open }))
        {
            await client.GetAsync("http://core/api/ips/payments/status");
        }

        Assert.Equal("Bearer secret-1", server.Requests[0].Authorization);
        Assert.Null(open.Requests[0].Authorization);
    }

    [Fact]
    public void A_token_matches_any_listed_token_and_nothing_else()
    {
        string[] tokens = ["old-token", "new-token"];

        Assert.True(IpsApiToken.Matches("Bearer new-token", tokens));
        Assert.True(IpsApiToken.Matches("bearer old-token", tokens));
        Assert.False(IpsApiToken.Matches("Bearer new-token2", tokens));
        Assert.False(IpsApiToken.Matches("new-token", tokens));
        Assert.False(IpsApiToken.Matches(null, tokens));
        Assert.False(IpsApiToken.Matches("Bearer new-token", []));
    }

    private static IpsSignedRequest Sign(byte[] body, X509Certificate2 certificate, IpsSignatureAlgorithm algorithm)
    {
        var digest = IpsContentSignature.Digest(body);
        return new IpsSignedRequest(digest, IpsContentSignature.Sign(digest, certificate, algorithm), IpsContentSignature.Name(algorithm), certificate.Thumbprint);
    }

    private static IpsSignatureVerifier Verifier(X509Certificate2 certificate) =>
        new([PublicOnly(certificate)], Enum.GetValues<IpsSignatureAlgorithm>(), new FixedTime(Now));

    // The gateway trusts the public certificate only.
    private static X509Certificate2 PublicOnly(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));

    private static X509Certificate2 Certificate(IpsSignatureAlgorithm algorithm)
    {
        var notBefore = Now.AddDays(-1);
        var notAfter = Now.AddYears(1);
        if (algorithm == IpsSignatureAlgorithm.ES256)
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return new CertificateRequest("CN=Core ES256", ecdsa, HashAlgorithmName.SHA256).CreateSelfSigned(notBefore, notAfter);
        }

        using var rsa = RSA.Create(2048);
        return new CertificateRequest("CN=Core RSA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(notBefore, notAfter);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Received(IpsSignedRequest Signed, byte[] Body, string? ContentType, string? Authorization);

    private sealed class Capture : HttpMessageHandler
    {
        public List<Received> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new Received(
                new IpsSignedRequest(Header(request, IpsSecurityHeaders.ContentSha256), Header(request, IpsSecurityHeaders.Signature),
                    Header(request, IpsSecurityHeaders.SignatureAlgorithm), Header(request, IpsSecurityHeaders.Certificate)),
                body,
                request.Content?.Headers.ContentType?.ToString(),
                Header(request, IpsSecurityHeaders.Authorization)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
