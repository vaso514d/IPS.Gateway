using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.IntegrationTests.Transport;
using IPS.MiidleWear.Contracts.Proxy;
using IPS.MiidleWear.CoreAPI;
using IPS.MiidleWear.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using HttpEndpointSettings = IPS.Middleware.Infrastructure.Transport.HttpEndpointSettings;

namespace IPS.Middleware.IntegrationTests.Security;

// Specification 021 through the real hosts. Each service requires what its Server section configures on the endpoints
// marked [RequireIpsAuthentication]: the gateway on its payment and proxy controllers, CoreAPI (a core system) on its
// receiver. Each client sends what the other side's address section configures: the gateway's Cbs endpoint, CoreAPI's
// Gateway section.
public sealed class CoreAuthenticationTests
{
    // A route marked [RequireIpsAuthentication] that needs no database: proxy management. Its empty request fails
    // validation (400) before the Proxy Solution is called, so 400 means the request got past authentication.
    private const string Send = ProxyRestApiRoutes.Register;
    // The bank of IncomingHttpClientTests.Services.
    private const string Participant = "TESTGE22";
    private const string Body = "{}";

    [Fact]
    public async Task With_a_trusted_certificate_the_gateway_refuses_what_is_not_signed_by_it()
    {
        using var core = Certificate("CN=Core");
        using var stranger = Certificate("CN=Stranger");
        using var gateway = new GatewayFactory(new() { ["Server:Signing:TrustedCertificates:0:Pem"] = core.ExportCertificatePem() });

        using var unsigned = await gateway.CreateClient().PostAsync(Send, Json(Body));
        using var foreign = await Client(gateway, stranger).PostAsync(Send, Json(Body));
        using var signed = await Client(gateway, core).PostAsync(Send, Json(Body));
        using var health = await gateway.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, signed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task A_body_changed_after_signing_is_refused()
    {
        using var core = Certificate("CN=Core");
        using var gateway = new GatewayFactory(new() { ["Server:Signing:TrustedCertificates:0:Pem"] = core.ExportCertificatePem() });
        var digest = IpsContentSignature.Digest(Encoding.UTF8.GetBytes(Body));
        using var request = new HttpRequestMessage(HttpMethod.Post, Send) { Content = Json("""{"changed":true}""") };
        request.Headers.Add(IpsSecurityHeaders.ContentSha256, digest);
        request.Headers.Add(IpsSecurityHeaders.Signature, IpsContentSignature.Sign(digest, core, IpsSignatureAlgorithm.RS256));
        request.Headers.Add(IpsSecurityHeaders.SignatureAlgorithm, "RS256");
        request.Headers.Add(IpsSecurityHeaders.Certificate, core.Thumbprint);

        using var response = await gateway.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // What is configured is required: with a token and a certificate both, either alone is refused.
    [Fact]
    public async Task A_gateway_configured_with_a_token_and_a_certificate_requires_both()
    {
        using var core = Certificate("CN=Core");
        using var gateway = new GatewayFactory(new()
        {
            ["Server:ApiTokens:0"] = "core-token",
            ["Server:Signing:TrustedCertificates:0:Pem"] = core.ExportCertificatePem()
        });

        using var tokenOnly = await Client(gateway, null, "core-token").PostAsync(Send, Json(Body));
        using var signatureOnly = await Client(gateway, core).PostAsync(Send, Json(Body));
        using var both = await Client(gateway, core, "core-token").PostAsync(Send, Json(Body));

        Assert.Equal(HttpStatusCode.Unauthorized, tokenOnly.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signatureOnly.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
    }

    [Fact]
    public async Task Without_server_authentication_the_gateway_api_is_open()
    {
        using var gateway = new GatewayFactory([]);

        using var unsigned = await gateway.CreateClient().PostAsync(Send, Json(Body));

        Assert.Equal(HttpStatusCode.BadRequest, unsigned.StatusCode);
    }

    // The token and the signing certificate sent to the core system belong under its address; anywhere else they stop
    // startup instead of being ignored.
    [Theory]
    [InlineData("Payments:Outgoing:Transport:Ips:ApiToken", "secret")]
    [InlineData("Proxy:Endpoint:Signing:Thumbprint", "0000")]
    public void A_core_system_setting_under_another_address_fails_startup(string key, string value)
    {
        using var gateway = new GatewayFactory(new() { [key] = value });

        var error = Assert.Throws<InvalidOperationException>(() => gateway.CreateClient());

        Assert.Contains("belong to the Cbs endpoints", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("token-1", HttpStatusCode.OK)]
    [InlineData("token-2", HttpStatusCode.OK)]
    [InlineData("wrong", HttpStatusCode.Unauthorized)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task The_core_receiver_accepts_the_gateway_only_with_a_listed_token(string? token, HttpStatusCode expected)
    {
        await using var core = await CoreApiAsync(new() { ["Server:ApiTokens:0"] = "token-1", ["Server:ApiTokens:1"] = "token-2" });
        using var certificates = new TransportCertificates();
        using var gateway = IncomingHttpClientTests.Services(WithCoreAuthentication(IncomingHttpClientTests.Settings(core.Urls.Single(), certificates), token, signing: null));

        var response = await gateway.GetRequiredService<IIncomingCoreClient>().SubmitAsync(IncomingHttpClientTests.Snapshot(Payment()), default);

        Assert.Equal((int)expected, response.StatusCode);
    }

    // The other direction is configured the same way: the gateway signs with Cbs:Signing, the core system trusts it in
    // its Server section.
    [Fact]
    public async Task The_core_receiver_accepts_the_gateway_only_with_its_signature()
    {
        using var gatewayCertificate = Certificate("CN=Gateway");
        var pfx = Path.Combine(Path.GetTempPath(), $"ips-021-{Guid.NewGuid():N}.pfx");
        await File.WriteAllBytesAsync(pfx, gatewayCertificate.Export(X509ContentType.Pfx, "secret"));
        try
        {
            await using var core = await CoreApiAsync(new() { ["Server:Signing:TrustedCertificates:0:Pem"] = gatewayCertificate.ExportCertificatePem() });
            using var certificates = new TransportCertificates();
            var settings = IncomingHttpClientTests.Settings(core.Urls.Single(), certificates);
            using var signedGateway = IncomingHttpClientTests.Services(WithCoreAuthentication(settings, token: null,
                new IpsSigningSettings { Path = pfx, Password = "secret", Algorithm = "PS256" }));
            using var unsignedGateway = IncomingHttpClientTests.Services(settings);

            var signed = await signedGateway.GetRequiredService<IIncomingCoreClient>().SubmitAsync(IncomingHttpClientTests.Snapshot(Payment()), default);
            var unsigned = await unsignedGateway.GetRequiredService<IIncomingCoreClient>().SubmitAsync(IncomingHttpClientTests.Snapshot(Payment()), default);

            Assert.Equal(200, signed.StatusCode);
            Assert.Equal(401, unsigned.StatusCode);
        }
        finally
        {
            File.Delete(pfx);
        }
    }

    [Fact]
    public async Task A_core_receiver_without_server_authentication_is_open()
    {
        await using var core = await CoreApiAsync([]);
        using var certificates = new TransportCertificates();
        using var gateway = IncomingHttpClientTests.Services(IncomingHttpClientTests.Settings(core.Urls.Single(), certificates));

        var response = await gateway.GetRequiredService<IIncomingCoreClient>().SubmitAsync(IncomingHttpClientTests.Snapshot(Payment()), default);

        Assert.Equal(200, response.StatusCode);
    }

    private static HttpClient Client(GatewayFactory gateway, X509Certificate2? certificate, string? token = null)
    {
        List<DelegatingHandler> handlers = [new IpsApiTokenHandler(token)];
        if (certificate is not null)
        {
            handlers.Add(new IpsSigningHandler(certificate, IpsSignatureAlgorithm.RS256));
        }

        return gateway.CreateDefaultClient([.. handlers]);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static Pacs008Request Payment() => new()
    {
        EndToEndId = "E2E-021",
        InstructionId = "INSTR-021",
        Amount = 12.34m,
        Currency = "GEL"
    };

    private static IncomingTransportSettings WithCoreAuthentication(IncomingTransportSettings settings, string? token, IpsSigningSettings? signing) => new()
    {
        Enabled = settings.Enabled,
        IpsSignatureTrust = settings.IpsSignatureTrust,
        ReceiveTimeout = settings.ReceiveTimeout,
        Ips = settings.Ips,
        Cbs = new HttpEndpointSettings
        {
            BaseUrl = settings.Cbs.BaseUrl,
            ConnectTimeout = settings.Cbs.ConnectTimeout,
            RequestTimeout = settings.Cbs.RequestTimeout,
            CircuitBreaker = settings.Cbs.CircuitBreaker,
            CheckCertificateRevocation = false,
            ApiToken = token,
            Signing = signing
        }
    };

    private static async Task<Microsoft.AspNetCore.Builder.WebApplication> CoreApiAsync(Dictionary<string, string?> settings)
    {
        var app = SampleApp.Create([], builder =>
        {
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(settings);
        });
        await app.StartAsync();
        return app;
    }

    private static X509Certificate2 Certificate(string subject)
    {
        using var rsa = RSA.Create(2048);
        return new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    // A Development gateway with proxy management on, so a marked route exists without a database.
    private sealed class GatewayFactory(Dictionary<string, string> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Proxy:Enabled", "true");
            builder.UseSetting("Bank:BicCode", "ITSBGE22");
            builder.UseSetting("Bank:NbgCode", "NBGEGE22");
            builder.UseSetting("Proxy:Endpoint:BaseUrl", "http://127.0.0.1:9");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
