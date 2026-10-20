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

// Specification 021 through the real hosts: the gateway checks the core system's signature on the routes marked
// [RequireCoreSignature], and the core system (CoreAPI on IPS.MiidleWear.AspNetCore) checks the gateway's static API token.
public sealed class CoreAuthenticationTests
{
    // A route marked [RequireCoreSignature] that needs no database: proxy management. Its empty request fails validation
    // (400) before the Proxy Solution is called, so 400 means the request got past the signature check.
    private const string Send = ProxyRestApiRoutes.Register;
    // The bank of IncomingHttpClientTests.Services.
    private const string Participant = "TESTGE22";
    private const string Body = "{}";

    [Fact]
    public async Task With_a_trusted_certificate_the_gateway_refuses_what_is_not_signed_by_it()
    {
        using var core = Certificate("CN=Core");
        using var stranger = Certificate("CN=Stranger");
        using var gateway = new GatewayFactory(core);

        using var unsigned = await gateway.CreateClient().PostAsync(Send, Json(Body));
        using var foreign = await Signed(gateway, stranger).PostAsync(Send, Json(Body));
        using var signed = await Signed(gateway, core).PostAsync(Send, Json(Body));
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
        using var gateway = new GatewayFactory(core);
        var digest = IpsContentSignature.Digest(Encoding.UTF8.GetBytes(Body));
        using var request = new HttpRequestMessage(HttpMethod.Post, Send) { Content = Json("""{"changed":true}""") };
        request.Headers.Add(IpsSecurityHeaders.ContentSha256, digest);
        request.Headers.Add(IpsSecurityHeaders.Signature, IpsContentSignature.Sign(digest, core, IpsSignatureAlgorithm.RS256));
        request.Headers.Add(IpsSecurityHeaders.SignatureAlgorithm, "RS256");
        request.Headers.Add(IpsSecurityHeaders.Certificate, core.Thumbprint);

        using var response = await gateway.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_trusted_certificate_the_gateway_api_is_open()
    {
        using var gateway = new GatewayFactory(trusted: null);

        using var unsigned = await gateway.CreateClient().PostAsync(Send, Json(Body));

        Assert.Equal(HttpStatusCode.BadRequest, unsigned.StatusCode);
    }

    [Theory]
    [InlineData("token-1", HttpStatusCode.OK)]
    [InlineData("token-2", HttpStatusCode.OK)]
    [InlineData("wrong", HttpStatusCode.Unauthorized)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task The_core_receiver_accepts_the_gateway_only_with_a_listed_token(string? token, HttpStatusCode expected)
    {
        await using var core = await CoreApiAsync(["token-1", "token-2"]);
        using var certificates = new TransportCertificates();
        using var gateway = IncomingHttpClientTests.Services(WithToken(IncomingHttpClientTests.Settings(core.Urls.Single(), certificates), token));

        var response = await gateway.GetRequiredService<IIncomingCoreClient>().SubmitAsync(Participant, Payment(), default);

        Assert.Equal((int)expected, response.StatusCode);
    }

    [Fact]
    public async Task A_core_receiver_without_tokens_is_open()
    {
        await using var core = await CoreApiAsync([]);
        using var certificates = new TransportCertificates();
        using var gateway = IncomingHttpClientTests.Services(IncomingHttpClientTests.Settings(core.Urls.Single(), certificates));

        var response = await gateway.GetRequiredService<IIncomingCoreClient>().SubmitAsync(Participant, Payment(), default);

        Assert.Equal(200, response.StatusCode);
    }

    private static HttpClient Signed(GatewayFactory gateway, X509Certificate2 certificate) =>
        gateway.CreateDefaultClient(new IpsSigningHandler(certificate, IpsSignatureAlgorithm.RS256));

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static Pacs008Request Payment() => new()
    {
        EndToEndId = "E2E-021",
        InstructionId = "INSTR-021",
        Amount = 12.34m,
        Currency = "GEL"
    };

    private static IncomingTransportSettings WithToken(IncomingTransportSettings settings, string? token) => new()
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
            ApiToken = token
        }
    };

    private static async Task<Microsoft.AspNetCore.Builder.WebApplication> CoreApiAsync(string[] tokens)
    {
        var settings = new Dictionary<string, string?>();
        for (var index = 0; index < tokens.Length; index++)
        {
            settings[$"Sample:ApiTokens:{index}"] = tokens[index];
        }

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

    // The gateway trusts the core system's public certificate only, given in configuration as PEM.
    private sealed class GatewayFactory(X509Certificate2? trusted) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Proxy:Enabled", "true");
            builder.UseSetting("Bank:BicCode", "ITSBGE22");
            builder.UseSetting("Bank:NbgCode", "NBGEGE22");
            builder.UseSetting("Proxy:Endpoint:BaseUrl", "http://127.0.0.1:9");
            if (trusted is not null)
            {
                builder.UseSetting("CoreAuthentication:TrustedCertificates:0:Pem", trusted.ExportCertificatePem());
            }
        }
    }
}
