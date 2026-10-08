using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Transport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transport;

public sealed class IncomingTransportConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pem_and_encrypted_pem_load_matching_private_keys(bool encrypted)
    {
        using var fixture = new TransportCertificates();
        using var loaded = fixture.SavePem(encrypted).Load(true, DateTimeOffset.UtcNow);
        Assert.Equal(fixture.Client.RawData, loaded.RawData);
        using var key = loaded.GetECDsaPrivateKey()!;
        using var publicKey = fixture.Client.GetECDsaPublicKey()!;
        var signature = key.SignData([1, 2, 3], HashAlgorithmName.SHA256);
        Assert.True(publicKey.VerifyData(new byte[] { 1, 2, 3 }, signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Pfx_public_pem_and_missing_store_thumbprint_are_checked()
    {
        using var fixture = new TransportCertificates();
        using var loaded = fixture.Identity.Load(true, DateTimeOffset.UtcNow);
        Assert.True(loaded.HasPrivateKey);
        using var trusted = fixture.Trust.Load(false, DateTimeOffset.UtcNow);
        Assert.False(trusted.HasPrivateKey);
        Assert.Throws<InvalidOperationException>(() => fixture.Identity.Load(true, DateTimeOffset.UtcNow.AddYears(1)));
        Assert.ThrowsAny<CryptographicException>(() => new CertificateSettings { Path = fixture.Identity.Path, Password = "wrong" }.Load(true, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => new CertificateSettings { Path = fixture.Trust.Path, Thumbprint = "123" }.Load(false, DateTimeOffset.UtcNow));
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<InvalidOperationException>(() => new CertificateSettings
            { Thumbprint = new string('0', 40), StoreLocation = StoreLocation.CurrentUser }.Load(false, DateTimeOffset.UtcNow));
        }
    }

    [Theory]
    [InlineData("ParticipantBic", "bad")]
    [InlineData("Ips:BaseUrl", "http://external.example")]
    [InlineData("MessagePath", "https://other.example/Message")]
    [InlineData("MessagePath", "//other.example/Message")]
    [InlineData("AckPath", "https://other.example/MessageAck")]
    [InlineData("AckPath", "//other.example/MessageAck")]
    [InlineData("Ips:ConnectionLimit", "1")]
    [InlineData("Cbs:ConnectionLimit", "2")]
    [InlineData("Ips:ConnectTimeout", "00:00:21")]
    [InlineData("Ips:CircuitBreaker:MinimumThroughput", "1")]
    [InlineData("Ips:CircuitBreaker:FailureRatio", "0")]
    [InlineData("ReceiveTimeout", "00:00:01")]
    [InlineData("Ips:UnexpectedSetting", "invalid")]
    public void Enabled_host_rejects_invalid_transport_configuration_at_startup(string key, string value)
    {
        using var fixture = new TransportCertificates();
        var settings = HostSettings(fixture);
        settings["Payments:Incoming:Transport:" + key] = value;
        using var factory = new ConfiguredHost(settings);
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Enabled_host_loads_separate_certificate_sources_without_starting_workers_or_connecting_to_sql()
    {
        using var fixture = new TransportCertificates();
        var settings = HostSettings(fixture);
        settings["Payments:Incoming:Transport:SigningCertificate:Path"] = fixture.Identity.Path;
        settings["Payments:Incoming:Transport:SigningCertificate:Password"] = fixture.Identity.Password;
        using var factory = new ConfiguredHost(settings);
        using var http = factory.CreateClient();
        Assert.Equal("Healthy", await http.GetStringAsync("/health/live"));
        var certificates = factory.Services.GetRequiredService<IncomingTransportCertificates>();
        Assert.Equal(fixture.Client.RawData, (await certificates.GetCurrentAsync(default))!.RawData);
        Assert.Equal(fixture.Client.RawData, Assert.Single(certificates.IpsSignatureTrust).RawData);
        var workers = factory.Services.GetServices<IHostedService>()
            .OfType<IPS.Middleware.Infrastructure.Inbound.Workers.IncomingWorker>().ToArray();
        Assert.Equal(4, workers.Length);
        await Task.WhenAll(workers.Select(worker => worker.ExecuteTask!));
    }

    [Theory]
    [InlineData("ConnectionStrings:Middleware", "")]
    [InlineData("ConnectionStrings:Middleware", "Server=only-server")]
    [InlineData("Payments:Signing:AllowUnsignedInDevelopment", "false")]
    [InlineData("Payments:Incoming:Transport:SigningCertificate:Path", "missing-file.pem")]
    public void Enabled_host_requires_usable_database_and_certificate_configuration(string key, string value)
    {
        using var fixture = new TransportCertificates();
        var settings = HostSettings(fixture);
        settings[key] = value;
        using var factory = new ConfiguredHost(settings);
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Disabled_transport_cannot_send_even_if_a_caller_resolves_its_port()
    {
        using var factory = new ConfiguredHost(new());
        var client = factory.Services.GetRequiredService<IIncomingReceiveClient>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReceiveAsync(default));
    }

    [Fact]
    public void Host_rejects_signature_trust_incompatible_with_the_protocol()
    {
        using var fixture = new TransportCertificates();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Incompatible IPS", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var rsa = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var source = fixture.SavePublic(rsa, "rsa-trust.pem");
        var settings = HostSettings(fixture);
        settings["Payments:Incoming:Transport:IpsSignatureTrust:0:Path"] = source.Path;
        using var factory = new ConfiguredHost(settings);
        var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("ECDSA public keys", failure.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> HostSettings(TransportCertificates fixture) => new()
    {
        ["Payments:Incoming:Transport:Enabled"] = "true",
        ["Payments:Incoming:Transport:ParticipantBic"] = "TESTGE22",
        ["Payments:Incoming:Transport:Ips:BaseUrl"] = "http://127.0.0.1:1",
        ["Payments:Incoming:Transport:Cbs:BaseUrl"] = "http://127.0.0.1:1",
        ["Payments:Incoming:Transport:IpsSignatureTrust:0:Path"] = fixture.SignatureTrust.Path,
        ["Payments:Signing:AllowUnsignedInDevelopment"] = "true",
        ["ConnectionStrings:Middleware"] = "Server=unreachable;Database=not-used;Integrated Security=true"
    };

    private sealed class ConfiguredHost(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}
