var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => TypedResults.Text("Healthy")).WithName("Liveness");
app.Run();

public partial class Program;
