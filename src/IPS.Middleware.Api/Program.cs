using IPS.Middleware.Api.Configuration;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddPaymentSettings();
builder.Services.AddIncomingTransportConfiguration();
builder.Services.AddIncomingWorkerConfiguration();
builder.Services.AddSingleton(services => new Pacs008SigningPolicy(
    services.GetRequiredService<IConfiguration>().GetValue<bool>(Pacs008SigningPolicy.AllowUnsignedConfigurationKey),
    services.GetRequiredService<IHostEnvironment>().IsDevelopment()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Pacs008MessageSigner>();

var app = builder.Build();
app.Services.ValidatePaymentSettings();
// Validate the signing policy after all host configuration providers have been applied.
_ = app.Services.GetRequiredService<Pacs008SigningPolicy>();
app.Services.ValidateIncomingTransport();
app.Services.ValidateIncomingWorkers();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => TypedResults.Text("Healthy")).WithName("Liveness");
app.Run();

public partial class Program;
