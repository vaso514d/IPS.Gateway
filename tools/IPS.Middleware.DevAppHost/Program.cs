using IPS.Middleware.AppHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var mode = builder.Configuration["DevHost:Mode"] ?? "Local";

IResourceBuilder<IResourceWithConnectionString>? database = builder.Configuration.GetConnectionString("Middleware") is not null
    ? builder.AddConnectionString("Middleware")
    : mode == "Local" ? AddSqlContainer(builder, mode) : null;
var core = builder.AddProject<Projects.IPS_MiidleWear_CoreAPI>("core", launchProfileName: null)
    .WithHttpEndpoint(port: 5280, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithUrlForEndpoint("http", url => url.Url = "/swagger");
var coreUrl = core.GetEndpoint("http");

IResourceBuilder<ProjectResource> api;
api = mode switch
{
    "Local" => AddLocalApi(builder, coreUrl),
    // Everything else, the CBS URL included, comes from the API's Uat launch profile and appsettings.json.
    "Uat" => builder.AddProject<Projects.IPS_Middleware_Api>("api", launchProfileName: "Uat"),
    _ => throw new InvalidOperationException($"DevHost:Mode must be Local or Uat, not {mode}."),
};
if (database is not null)
{
    api.WithReference(database)
        .WaitFor(database);
}

api.WaitFor(core)
    .WithHttpHealthCheck("/health/live");

builder.Build().Run();

// SQL Server in a persistent container with a volume, so data survives restarts; each mode has its own database.
static IResourceBuilder<IResourceWithConnectionString> AddSqlContainer(IDistributedApplicationBuilder builder, string mode) =>
    builder.AddSqlServer("sql")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume("ips-middleware-dev-sql")
        .AddDatabase("Middleware", databaseName: "IpsGateway" + mode);

// The simulators stand in for IPS and the Proxy Solution over TLS with a certificate generated for the run, which the API
// trusts by file; the simulated IPS answers as NBGEGE22 to BAGAGE22, so the API takes those bank codes.
static IResourceBuilder<ProjectResource> AddLocalApi(
    IDistributedApplicationBuilder builder,
    EndpointReference coreUrl)
{
    var temporary = new TemporaryDirectories();
    builder.Services.AddSingleton<IHostedService>(temporary);
    var certificates = DevelopmentCertificates.Create();
    temporary.Add(certificates.Directory);

    var simulators = builder.AddProject<Projects.IPS_Middleware_Simulators>("simulators", launchProfileName: null)
        .WithHttpEndpoint(name: "http")
        .WithHttpsEndpoint(name: "https")
        .WithEnvironment("Kestrel__Certificates__Default__Path", certificates.ServerPfx)
        .WithEnvironment("Kestrel__Certificates__Default__Password", certificates.Password)
        .WithEnvironment("Simulator__IpsSigningCertificate__Path", certificates.IpsSigningPfx)
        .WithEnvironment("Simulator__IpsSigningCertificate__Password", certificates.Password)
        .WithHttpHealthCheck("/health", endpointName: "http");
    var simulatorUrl = simulators.GetEndpoint("https");

    return builder.AddProject<Projects.IPS_Middleware_Api>("api", launchProfileName: null)
        .WithHttpEndpoint(port: 5080, name: "http")
        .WaitFor(simulators)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithEnvironment("Database__MigrateOnStartup", "true")
        .WithEnvironment("Bank__BicCode", "BAGAGE22")
        .WithEnvironment("Bank__NbgCode", "NBGEGE22")
        .WithEnvironment("Payments__Signing__AllowUnsignedInDevelopment", "true")
        .WithEnvironment("Payments__Outgoing__Transport__Enabled", "true")
        .WithEnvironment("Payments__Outgoing__Transport__Ips__BaseUrl", simulatorUrl)
        .WithEnvironment("Payments__Outgoing__Transport__Ips__ServerTrust__0__Path", certificates.ServerPem)
        .WithEnvironment("Payments__Outgoing__Transport__Ips__CheckCertificateRevocation", "false")
        .WithEnvironment("Payments__Outgoing__Transport__IpsSignatureTrust__0__Path", certificates.IpsSigningPem)
        .WithEnvironment("Payments__Outgoing__Transport__Cbs__BaseUrl", coreUrl)
        .WithEnvironment("Payments__Outgoing__Execution__Enabled", "true")
        .WithEnvironment("Payments__Outgoing__Policy__Currencies__0__Code", "GEL")
        .WithEnvironment("Proxy__Enabled", "true")
        .WithEnvironment("Proxy__Endpoint__BaseUrl", simulatorUrl)
        .WithEnvironment("Proxy__Endpoint__ServerTrust__0__Path", certificates.ServerPem)
        .WithEnvironment("Proxy__Endpoint__CheckCertificateRevocation", "false");
}
