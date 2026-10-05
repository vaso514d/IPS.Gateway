using System.Text.Json.Serialization;
using IPS.Middleware.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddMiddleware();

var app = builder.Build();
app.Services.ValidateMiddleware();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => TypedResults.Text("Healthy")).WithName("Liveness");
app.MapControllers();
app.Run();

public partial class Program;
