using IPS.MiidleWear.AspNetCore;

namespace IPS.MiidleWear.CoreReceiver.Sample;

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
        configure?.Invoke(builder);

        var app = builder.Build();
        app.MapIpsCoreReceiver();
        // For people trying the sample: what the gateway delivered and what the core answered.
        app.MapGet("/sample/ledger", (CoreLedger ledger) => ledger.Snapshot());
        return app;
    }
}
