using IPS.MiidleWear.AspNetCore;

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
        // The receiver's own document, exactly as a bank's core system serves it, and the sample's extras in v1.
        builder.Services.AddIpsCoreReceiverOpenApi();
        builder.Services.AddOpenApi();
        configure?.Invoke(builder);

        var app = builder.Build();
        app.MapIpsCoreReceiver();
        // For people trying the sample: what the gateway delivered and what the core answered.
        app.MapGet("/sample/ledger", (CoreLedger ledger) => ledger.Snapshot());
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint($"/openapi/{CoreReceiverOpenApi.DocumentName}.json", "IPS core receiver");
            options.SwaggerEndpoint("/openapi/v1.json", "Sample extras");
        });
        app.MapGet("/", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();
        return app;
    }
}
