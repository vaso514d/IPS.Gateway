using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Payments.Transport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transport;

public sealed class OutgoingTransportConfigurationTests
{
    [Theory]
    [InlineData("ParticipantBic", "bad")]
    [InlineData("Ips:BaseUrl", "http://external.example")]
    [InlineData("MessagePath", "//other.example")]
    [InlineData("CallbackPath", "https://other.example")]
    [InlineData("Ips:ConnectionLimit", "0")]
    [InlineData("Ips:ConnectTimeout", "00:00:26")]
    [InlineData("Ips:RequestTimeout", "00:00:00")]
    [InlineData("Cbs:CircuitBreaker:MinimumThroughput", "1")]
    [InlineData("Unexpected", "true")]
    public void Invalid_configuration_is_rejected_before_serving(string key, string value)
    {
        using var certificates = new TransportCertificates();
        var settings = Settings(certificates);
        settings["Payments:Outgoing:Transport:" + key] = value;
        using var host = new Host(settings);
        Assert.ThrowsAny<Exception>(() => host.CreateClient());
    }

    [Fact]
    public async Task Outgoing_only_startup_loads_certificates_without_enabling_incoming_or_opening_sql()
    {
        using var certificates = new TransportCertificates();
        using var host = new Host(Settings(certificates));
        using var http = host.CreateClient();
        Assert.Equal("Healthy", await http.GetStringAsync("/health/live"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.PostAsync("/api/ips/pacs008/send", null)).StatusCode);
        Assert.Single(host.Services.GetRequiredService<OutgoingTransportCertificates>().IpsSignatureTrust);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Services.GetRequiredService<IIncomingReceiveClient>().ReceiveAsync(default));
    }

    [Fact]
    public async Task Disabled_outgoing_port_cannot_send()
    {
        using var host = new Host(new());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Services.GetRequiredService<IIpsTransport>().SendAsync("<payment/>", default));
    }

    [Theory]
    [InlineData("ConnectionStrings:Middleware", "")]
    [InlineData("Payments:Signing:AllowUnsignedInDevelopment", "false")]
    [InlineData("Payments:Outgoing:Transport:IpsSignatureTrust:0:Path", "missing.pem")]
    [InlineData("Payments:Outgoing:Pacs008:PersistenceBudget", "00:00:45")]
    public void Enabled_configuration_requires_database_signing_trust_and_bounded_persistence(string key, string value)
    {
        using var certificates = new TransportCertificates();
        var settings = Settings(certificates);
        settings[key] = value;
        using var host = new Host(settings);
        Assert.ThrowsAny<Exception>(() => host.CreateClient());
    }

    [Fact]
    public async Task Both_directions_keep_their_own_signing_identity_and_trust()
    {
        using var incoming = new TransportCertificates();
        using var outgoing = new TransportCertificates();
        var settings = Settings(outgoing);
        settings["Payments:Outgoing:Transport:SigningCertificate:Path"] = outgoing.Identity.Path;
        settings["Payments:Outgoing:Transport:SigningCertificate:Password"] = outgoing.Identity.Password;
        settings["Payments:Incoming:Transport:Enabled"] = "true";
        settings["Payments:Incoming:Transport:ParticipantBic"] = "TESTGE22";
        settings["Payments:Incoming:Transport:Ips:BaseUrl"] = "http://127.0.0.1:1";
        settings["Payments:Incoming:Transport:Cbs:BaseUrl"] = "http://127.0.0.1:1";
        settings["Payments:Incoming:Transport:SigningCertificate:Path"] = incoming.Identity.Path;
        settings["Payments:Incoming:Transport:SigningCertificate:Password"] = incoming.Identity.Password;
        settings["Payments:Incoming:Transport:IpsSignatureTrust:0:Path"] = incoming.SignatureTrust.Path;
        using var host = new Host(settings);
        using var http = host.CreateClient();
        var inbound = host.Services.GetRequiredService<IncomingTransportCertificates>();
        var outbound = host.Services.GetRequiredService<OutgoingTransportCertificates>();
        Assert.Equal(incoming.Client.RawData, (await inbound.GetCurrentAsync(default))!.RawData);
        Assert.Equal(outgoing.Client.RawData, (await outbound.GetCurrentAsync(default))!.RawData);
        Assert.Equal(incoming.Client.RawData, Assert.Single(inbound.IpsSignatureTrust).RawData);
        Assert.Equal(outgoing.Client.RawData, Assert.Single(outbound.IpsSignatureTrust).RawData);
    }

    private static Dictionary<string, string?> Settings(TransportCertificates certificates) => new()
    {
        ["Payments:Outgoing:Transport:Enabled"] = "true",
        ["Payments:Outgoing:Transport:ParticipantBic"] = "TESTGE22",
        ["Payments:Outgoing:Transport:Ips:BaseUrl"] = "http://127.0.0.1:1",
        ["Payments:Outgoing:Transport:Cbs:BaseUrl"] = "http://127.0.0.1:1",
        ["Payments:Outgoing:Transport:IpsSignatureTrust:0:Path"] = certificates.SignatureTrust.Path,
        ["Payments:Signing:AllowUnsignedInDevelopment"] = "true",
        ["ConnectionStrings:Middleware"] = "Server=unreachable;Database=not-used;Integrated Security=true"
    };
    private sealed class Host(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}
