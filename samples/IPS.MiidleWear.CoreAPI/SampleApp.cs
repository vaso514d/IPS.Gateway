using IPS.MiidleWear.AspNetCore;
using IPS.MiidleWear.CoreAPI.Sending;
using Microsoft.Extensions.Options;

namespace IPS.MiidleWear.CoreAPI;

// A named entry point instead of top-level statements, so tests can start the sample without a second global Program.
public static class SampleApp
{
    public static async Task Main(string[] args) => await Create(args).RunAsync();

    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<SampleOptions>(builder.Configuration.GetSection(SampleOptions.Section));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<CoreLedger>();
        builder.Services.AddIpsCoreReceiver<SampleReceiver>();
        // Read when first used, so it sees the final configuration.
        builder.Services.AddIpsApiTokenAuthentication(options =>
        {
            foreach (var token in builder.Configuration.GetSection(SampleOptions.Section).Get<SampleOptions>()?.ApiTokens ?? [])
            {
                options.Tokens.Add(token);
            }
        });
        // Sending: CoreAPI also plays the core system that sends transfers to the gateway.
        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.Section));
        builder.Services.AddSingleton<PaymentGenerator>();
        builder.Services.AddSingleton<SentPayments>();
        builder.Services.AddSingleton<GatewaySigning>();
        builder.Services.AddHttpClient<GatewaySender>((services, client) =>
        {
            var gateway = services.GetRequiredService<IOptions<GatewayOptions>>().Value;
            client.BaseAddress = new Uri(gateway.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = gateway.Timeout;
        }).AddHttpMessageHandler(services => services.GetRequiredService<GatewaySigning>().CreateHandler());
        // The receiver's own document, exactly as a bank's core system serves it; sending and the ledger are in v1.
        builder.Services.AddIpsCoreReceiverOpenApi();
        builder.Services.AddOpenApi(options => options.AddOperationTransformer(GatewayChoices.Apply));
        configure?.Invoke(builder);

        var app = builder.Build();
        // The gateway calls with its static API token (specification 021); without configured tokens the receiver is open.
        app.MapIpsCoreReceiver().RequireIpsApiToken();
        SendEndpoints.Map(app);
        // For people trying the sample: what the gateway delivered and what the core answered.
        app.MapGet("/sample/ledger", (CoreLedger ledger) => ledger.Snapshot());
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Send to the gateway, ledger");
            options.SwaggerEndpoint($"/openapi/{CoreReceiverOpenApi.DocumentName}.json", "IPS core receiver");
        });
        app.MapGet("/", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();
        return app;
    }
}
