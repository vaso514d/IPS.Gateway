using System.Text.Json.Serialization;
using IPS.Middleware.Api.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddMiddleware();

var app = builder.Build();
app.Services.ValidateMiddleware();
await app.Services.MigrateDatabaseAsync(app.Lifetime.ApplicationStopping);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => TypedResults.Text("Healthy")).WithName("Liveness");
app.MapHealthChecks(DiagnosticsConfiguration.ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(DiagnosticsConfiguration.ReadyTag) });
app.MapControllers();
app.Run();

public partial class Program;
