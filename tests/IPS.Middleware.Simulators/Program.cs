using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Simulators;

// Test support only: a stand-in for IPS, the CBS callback and recall receive routes and the Proxy Solution, with a control API under /_sim. It is not part
// of the deployable service.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SimulatorState>();
builder.Services.AddSingleton(SigningFromConfiguration);
builder.Services.AddSingleton<Pacs008MessageSigner>(_ => new(new Pacs008SigningPolicy(false, false), TimeProvider.System));

var app = builder.Build();
var state = app.Services.GetRequiredService<SimulatorState>();

app.MapGet("/health", () => "Healthy");

// IPS: every message is recorded; the answer follows the configured behaviour.
app.MapPost("/Message", async (HttpContext context, SimulatorSigning signing, Pacs008MessageSigner signer) =>
{
    var xml = await new StreamReader(context.Request.Body).ReadToEndAsync();
    state.Messages.Enqueue(new(xml, context.Request.Headers.ContainsKey("X-MONTRAN-RTP-PossibleDuplicate"),
        context.Request.Headers["X-MONTRAN-IPS-Version"], context.Request.Headers["X-MONTRAN-IPS-Channel"], DateTimeOffset.UtcNow));
    if (state.Delay > TimeSpan.Zero)
    {
        await Task.Delay(state.Delay, context.RequestAborted);
    }

    if (state.Block)
    {
        await state.WaitForReleaseAsync(context.RequestAborted);
    }

    if (state.TakeLostReply())
    {
        context.Abort();
        return Results.Empty;
    }

    if (state.TakeUnresolvedReply())
    {
        return Results.Text("raw upstream failure", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var original = Replies.Read(xml);
    var accepted = !state.Reject;
    context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = accepted ? "ACCP" : "RJCT/1009";
    var reply = Replies.Pacs002(original, accepted);
    return Results.Text(signer.PrepareReply(reply, signing.Certificate).Xml, "application/xml");
});

// CBS: the outgoing status callback.
app.MapPost("/api/ips/transactions/status/receive", async (HttpContext context) =>
{
    state.Callbacks.Enqueue(new(await new StreamReader(context.Request.Body).ReadToEndAsync(), DateTimeOffset.UtcNow));
    return Results.NoContent();
});

// CBS: the incoming recall, cancellation and recall refusal deliveries (012c). The core takes each one (ACCP).
foreach (var message in new[] { "camt056", "camt055", "camt029" })
{
    app.MapPost("/api/ips/" + message + "/receive", async (HttpContext context) =>
    {
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        state.CoreDeliveries.Enqueue(new(message, body, context.Request.Headers["Idempotency-Key"], DateTimeOffset.UtcNow));
        return Results.Json(new { status = "ACCP" });
    });
}

// Proxy Solution: the three management operations.
foreach (var operation in new[] { "register", "update", "remove" })
{
    app.MapPost("/PRX/" + operation, async (HttpContext context) =>
    {
        var xml = await new StreamReader(context.Request.Body).ReadToEndAsync();
        state.ProxyCalls.Enqueue(new(operation, xml, context.Request.Headers["X-MONTRAN-PRX-Channel"], DateTimeOffset.UtcNow));
        return Results.Text(Replies.ProxyReply(Replies.ProxyOperationId(xml), !state.RejectProxy), "application/xml");
    });
}

// Control API for tests.
app.MapGet("/_sim/received", () => new
{
    Messages = state.Messages.ToArray(),
    Callbacks = state.Callbacks.ToArray(),
    CoreDeliveries = state.CoreDeliveries.ToArray(),
    ProxyCalls = state.ProxyCalls.ToArray()
});
app.MapPost("/_sim/behaviour", (Behaviour behaviour) =>
{
    state.Reject = behaviour.Reject ?? state.Reject;
    state.RejectProxy = behaviour.RejectProxy ?? state.RejectProxy;
    state.Delay = behaviour.DelayMilliseconds is { } delay ? TimeSpan.FromMilliseconds(delay) : state.Delay;
    state.Block = behaviour.Block ?? state.Block;
    if (behaviour.LoseNext is { } lose)
    {
        state.LoseNext(lose);
    }

    if (behaviour.AnswerUnresolved is { } unresolved)
    {
        state.AnswerUnresolved(unresolved);
    }

    return Results.NoContent();
});
app.MapPost("/_sim/release", () =>
{
    state.Release();
    return Results.NoContent();
});
app.MapPost("/_sim/reset", () =>
{
    state.Reset();
    return Results.NoContent();
});

app.Run();

// The certificate the simulated IPS signs its answers with; the service trusts its public part.
static SimulatorSigning SigningFromConfiguration(IServiceProvider services)
{
    var section = services.GetRequiredService<IConfiguration>().GetSection("Simulator:IpsSigningCertificate");
    var path = section["Path"] ?? throw new InvalidOperationException("Simulator:IpsSigningCertificate:Path is required.");
    return new SimulatorSigning(X509CertificateLoader.LoadPkcs12FromFile(path, section["Password"], X509KeyStorageFlags.EphemeralKeySet));
}

internal sealed record SimulatorSigning(X509Certificate2 Certificate);

