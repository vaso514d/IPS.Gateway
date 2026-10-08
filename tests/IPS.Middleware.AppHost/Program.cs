using Aspire.Hosting.ApplicationModel;
using IPS.Middleware.AppHost;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// A whole deployment of the middleware for local runs and tests: SQL Server in a container, simulators standing in for IPS, the
// CBS and the Proxy Solution, and one or more instances of the API (as projects, or as containers built from the Dockerfile).
//   Middleware:Instances   number of API instances sharing the database (default 1)
//   Middleware:Container   run the API as a container (the published output with src/IPS.Middleware.Api/Dockerfile) instead of as a project (default false)
//   Middleware:Concurrency outgoing execution concurrency of each instance (default 4; the performance harness passes the shipped 8)
//   Middleware:Signing     sign outgoing messages with a generated key instead of sending them unsigned (default false)
var builder = DistributedApplication.CreateBuilder(args);
var instances = int.Parse(builder.Configuration["Middleware:Instances"] ?? "1", System.Globalization.CultureInfo.InvariantCulture);
var asContainer = bool.Parse(builder.Configuration["Middleware:Container"] ?? "false");
// A small admission limit leaves work waiting for the recovery sweeps of every instance, so instances share it.
var concurrency = int.Parse(builder.Configuration["Middleware:Concurrency"] ?? "4", System.Globalization.CultureInfo.InvariantCulture);
var signing = bool.Parse(builder.Configuration["Middleware:Signing"] ?? "false");

// The generated passwords are visible in the dashboard and in the container environment; they protect throwaway files only.
var temporary = new TemporaryDirectories();
builder.Services.AddSingleton<IHostedService>(temporary);
var certificates = DevelopmentCertificates.Create();
temporary.Add(certificates.Directory);

var sql = builder.AddSqlServer("sql");
var database = sql.AddDatabase("Middleware");

// The service never migrates at startup, so the database is brought to the current model once it exists.
builder.Eventing.Subscribe<ResourceReadyEvent>(database.Resource, async (_, cancellationToken) =>
{
    var connectionString = await database.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken)
        ?? throw new InvalidOperationException("The database has no connection string.");
    await using var context = new TransactionDbContext(new DbContextOptionsBuilder<TransactionDbContext>().UseSqlServer(connectionString).Options);
    await context.Database.MigrateAsync(cancellationToken);
});

var simulators = builder.AddProject<Projects.IPS_Middleware_Simulators>("simulators")
    .WithHttpEndpoint(name: "http")
    .WithHttpsEndpoint(name: "https")
    .WithEnvironment("Kestrel__Certificates__Default__Path", certificates.ServerPfx)
    .WithEnvironment("Kestrel__Certificates__Default__Password", certificates.Password)
    .WithEnvironment("Simulator__IpsSigningCertificate__Path", certificates.IpsSigningPfx)
    .WithEnvironment("Simulator__IpsSigningCertificate__Password", certificates.Password)
    .WithHttpHealthCheck("/health", endpointName: "http");

string? imageContext = null;
for (var number = 1; number <= instances; number++)
{
    var name = $"middleware-{number}";
    if (asContainer)
    {
        if (imageContext is null)
        {
            imageContext = ApiImage.Prepare(builder.AppHostDirectory);
            temporary.Add(imageContext);
        }

        var container = builder.AddDockerfile(name, contextPath: imageContext)
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithBindMount(certificates.TrustDirectory, "/certs", isReadOnly: true);
        if (signing)
        {
            container.WithBindMount(certificates.SigningDirectory, "/signing", isReadOnly: true);
        }

        Configure(container, name, number, concurrency, signing, certificates, simulators, database, certificateRoot: "/certs", signingRoot: "/signing");
        container.WithHttpHealthCheck("/health/ready").WithHttpHealthCheck("/health/live");
    }
    else
    {
        var project = builder.AddProject<Projects.IPS_Middleware_Api>(name, launchProfileName: null)
            .WithHttpEndpoint(name: "http");
        Configure(project, name, number, concurrency, signing, certificates, simulators, database, certificateRoot: certificates.TrustDirectory, signingRoot: certificates.SigningDirectory);
        project.WithHttpHealthCheck("/health/ready").WithHttpHealthCheck("/health/live");
    }
}

builder.Build().Run();

// Everything the service needs, as environment: transport to the simulators over TLS it trusts by file, the IPS signature trust,
// the proxy, the database and, with signing on, the key it signs with. Each instance sends a different protocol version, so a
// test can tell who sent what.
static void Configure<T>(
    IResourceBuilder<T> middleware,
    string name,
    int number,
    int concurrency,
    bool signing,
    DevelopmentCertificates certificates,
    IResourceBuilder<ProjectResource> simulators,
    IResourceBuilder<SqlServerDatabaseResource> database,
    string certificateRoot,
    string signingRoot)
    where T : IResourceWithEnvironment, IResourceWithWaitSupport
{
    var serverTrust = Path.Combine(certificateRoot, Path.GetFileName(certificates.ServerPem)).Replace('\\', '/');
    var ipsTrust = Path.Combine(certificateRoot, Path.GetFileName(certificates.IpsSigningPem)).Replace('\\', '/');
    var simulatorUrl = simulators.GetEndpoint("https");
    middleware
        .WithReference(database)
        .WaitFor(database)
        .WaitFor(simulators)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithEnvironment("Payments__Signing__AllowUnsignedInDevelopment", signing ? "false" : "true")
        .WithEnvironment("Payments__Outgoing__Transport__Enabled", "true")
        .WithEnvironment("Payments__Outgoing__Transport__ParticipantBic", "BAGAGE22")
        .WithEnvironment("Payments__Outgoing__Transport__IpsVersion", number.ToString())
        .WithEnvironment("Payments__Outgoing__Transport__Ips__BaseUrl", simulatorUrl)
        .WithEnvironment("Payments__Outgoing__Transport__Ips__ServerTrust__0__Path", serverTrust)
        .WithEnvironment("Payments__Outgoing__Transport__Ips__CheckCertificateRevocation", "false")
        .WithEnvironment("Payments__Outgoing__Transport__Ips__RequestTimeout", "00:00:20")
        .WithEnvironment("Payments__Outgoing__Transport__Cbs__BaseUrl", simulatorUrl)
        .WithEnvironment("Payments__Outgoing__Transport__Cbs__ServerTrust__0__Path", serverTrust)
        .WithEnvironment("Payments__Outgoing__Transport__Cbs__CheckCertificateRevocation", "false")
        .WithEnvironment("Payments__Outgoing__Transport__IpsSignatureTrust__0__Path", ipsTrust)
        .WithEnvironment("Payments__Outgoing__Execution__Enabled", "true")
        .WithEnvironment("Payments__Outgoing__Execution__Concurrency", concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .WithEnvironment("Payments__Outgoing__Execution__HttpWait", "00:00:21")
        .WithEnvironment("Payments__Outgoing__Execution__AttemptBudget", "00:00:22")
        .WithEnvironment("Payments__Outgoing__Pacs008__Ownership", "00:00:30")
        .WithEnvironment("Payments__Outgoing__Execution__DiscoveryInterval", "00:00:00.200")
        .WithEnvironment("Payments__Outgoing__StatusDelivery__DiscoveryInterval", "00:00:00.200")
        .WithEnvironment("Payments__Outgoing__Investigation__DiscoveryInterval", "00:00:00.200")
        .WithEnvironment("Payments__Outgoing__Policy__Currencies__0__Code", "GEL")
        .WithEnvironment("Payments__Outgoing__Protocol__IpsBic", "NBGEGE22")
        .WithEnvironment("Proxy__Enabled", "true")
        .WithEnvironment("Proxy__ParticipantBic", "BAGAGE22")
        .WithEnvironment("Proxy__ProxyBic", "PROXGE22")
        .WithEnvironment("Proxy__Endpoint__BaseUrl", simulatorUrl)
        .WithEnvironment("Proxy__Endpoint__ServerTrust__0__Path", serverTrust)
        .WithEnvironment("Proxy__Endpoint__CheckCertificateRevocation", "false");
    if (signing)
    {
        var signingKey = Path.Combine(signingRoot, Path.GetFileName(certificates.OutgoingSigningPfx)).Replace('\\', '/');
        middleware
            .WithEnvironment("Payments__Outgoing__Transport__SigningCertificate__Path", signingKey)
            .WithEnvironment("Payments__Outgoing__Transport__SigningCertificate__Password", certificates.Password);
    }
}
