using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.IntegrationTests.Transport;

internal sealed class HttpSimulator(WebApplication app) : IAsyncDisposable
{
    public string Url => app.Urls.Single();

    public static async Task<HttpSimulator> StartAsync(RequestDelegate handler, X509Certificate2? certificate = null,
        X509Certificate2? expectedClient = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listen =>
        {
            if (certificate is not null) listen.UseHttps(https =>
            {
                https.ServerCertificate = certificate;
                if (expectedClient is not null)
                {
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    https.ClientCertificateValidation = (client, _, _) => client.RawData.SequenceEqual(expectedClient.RawData);
                }
            });
        }));
        var app = builder.Build();
        app.Run(handler);
        await app.StartAsync();
        return new(app);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
