using IPS.MiidleWear.Contracts.Camt056;
using IPS.MiidleWear.Contracts.Pacs004;
using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Pacs009;

namespace IPS.MiidleWear.CoreAPI.Sending;

// CoreAPI as the sending core system: each endpoint generates a valid transfer and posts it to the gateway's send route.
// The optional query values replace the random ones. A recall quotes a pacs.008 sent earlier from here.
internal static class SendEndpoints
{
    private const string Tag = "Send to the gateway";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/core/send").WithTags(Tag);

        group.MapPost("/pacs008", async (string? currency, string? creditorBic, decimal? amount, PaymentGenerator generator,
                GatewaySender sender, SentPayments sent, CancellationToken cancellationToken) =>
            {
                var payment = generator.Pacs008(currency, creditorBic, amount);
                var result = await sender.SendAsync(Pacs008RestApiRoutes.Send, payment, cancellationToken);
                sent.Remember(payment, result);
                return result;
            })
            .WithName("SendPacs008")
            .WithSummary("Generate a customer credit transfer (pacs.008) and send it to the gateway.")
            .WithDescription("The transfer is remembered by its clientReference, so /core/send/camt056 can recall it.");

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

        group.MapPost("/camt056", async (string originalClientReference, string originalTransactionId, string? reasonCode,
                PaymentGenerator generator, GatewaySender sender, SentPayments sent, CancellationToken cancellationToken) =>
            {
                if (sent.Find(originalClientReference) is not { } original)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                        title: "No pacs.008 with this clientReference was sent since CoreAPI started.",
                        detail: "See GET /core/sent/pacs008.");
                }

                if (string.IsNullOrWhiteSpace(original.MessageId))
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                        title: "The gateway's answer to this pacs.008 had no messageId.",
                        detail: "Only a payment the gateway accepted for sending can be recalled.");
                }

                return Results.Ok(await sender.SendAsync(Camt056RestApiRoutes.Send,
                    generator.Camt056(original, originalTransactionId, reasonCode), cancellationToken));
            })
            .WithName("SendCamt056")
            .WithSummary("Recall a pacs.008 sent from CoreAPI (camt.056) through the gateway.")
            .WithDescription(
                "originalClientReference: the clientReference of a pacs.008 sent from /core/send/pacs008 (GET /core/sent/pacs008 lists " +
                "them). originalTransactionId: the TxId the gateway generated for it; the gateway's answer does not return it, so " +
                "take it from Pacs008Payments.TransactionId or the sent XML in OutgoingMessages. reasonCode: the recall reason.");

        endpoints.MapGet("/core/sent/pacs008", (SentPayments sent) => sent.Snapshot())
            .WithTags(Tag)
            .WithName("SentPacs008")
            .WithSummary("The pacs.008 transfers sent since CoreAPI started, which can be recalled.");
    }
}
