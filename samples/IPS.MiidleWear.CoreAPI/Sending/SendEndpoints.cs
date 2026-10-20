using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;

namespace IPS.MiidleWear.CoreAPI.Sending;

// CoreAPI as the sending core system: each endpoint generates a valid transfer and posts it to the gateway's send route.
// The optional query values replace the random ones.
internal static class SendEndpoints
{
    private const string Tag = "Send to the gateway";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/core/send").WithTags(Tag);

        group.MapPost("/pacs008", (string? currency, string? creditorBic, decimal? amount, PaymentGenerator generator,
                GatewaySender sender, CancellationToken cancellationToken) =>
                sender.SendAsync(Pacs008RestApiRoutes.Send, generator.Pacs008(currency, creditorBic, amount), cancellationToken))
            .WithName("SendPacs008")
            .WithSummary("Generate a customer credit transfer (pacs.008) and send it to the gateway.");

        group.MapPost("/pacs009", (DateOnly? valueDate, string? currency, string? creditorBic, decimal? amount,
                PaymentGenerator generator, GatewaySender sender, CancellationToken cancellationToken) =>
                sender.SendAsync(Pacs009RestApiRoutes.Send, generator.Pacs009(valueDate, currency, creditorBic, amount), cancellationToken))
            .WithName("SendPacs009")
            .WithSummary("Generate a financial institution transfer (pacs.009) and send it to the gateway.");

        group.MapPost("/pacs004", (DateOnly? valueDate, string? currency, string? creditorBic, decimal? amount,
                PaymentGenerator generator, GatewaySender sender, CancellationToken cancellationToken) =>
                sender.SendAsync(Pacs004RestApiRoutes.Send, generator.Pacs004(valueDate, currency, creditorBic, amount), cancellationToken))
            .WithName("SendPacs004")
            .WithSummary("Generate a payment return (pacs.004, reason FOCR) and send it to the gateway.")
            .WithDescription("creditorBic is the bank the money goes back to.");
    }
}
