using System.Text.Json.Serialization;
using IPS.Middleware.Api.Configuration;
using IPS.MiidleWear.Security.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddMiddleware();
builder.AddIpsServer();

var app = builder.Build();
app.Services.ValidateMiddleware();
app.Services.LogMiddlewareSettings(app.Logger);
await app.Services.MigrateDatabaseAsync(app.Lifetime.ApplicationStopping);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "IPS gateway"));
    app.MapGet("/", () => Results.Redirect("/swagger"))
        .ExcludeFromDescription();
}

app.MapGet("/health/live", () => TypedResults.Text("Healthy")).WithName("Liveness");
app.MapHealthChecks(DiagnosticsConfiguration.ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(DiagnosticsConfiguration.ReadyTag) });
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
