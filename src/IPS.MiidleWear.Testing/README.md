# IPS.MiidleWear.Testing

Conformance checks for a core banking system's receiver: the `IClientPaymentReceiver` endpoints the IPS middleware (the gateway) calls. The kit sends what the gateway sends, with its JSON (`null` members included) and its `Idempotency-Key`, and reads every answer with the gateway's rules. It has no test-framework dependency.

```csharp
// xUnit, with the receiver in process
public sealed class ReceiverConformanceTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task The_receiver_meets_the_gateway_contract()
    {
        using var client = factory.CreateClient();
        var result = await IpsReceiverConformance.RunAsync(client, new() { ParticipantBic = "MYBKGE22", CreditAccount = "GE00MY0000000000000001" });
        result.EnsurePassed();   // throws with every failed check and its reason
    }
}
```

The same call works against a deployed receiver: create an `HttpClient` whose `BaseAddress` is the URL the gateway is configured with.

## Checks

For each of the seven receive kinds (pacs.008, pacs.009, pacs.004, pain.001, camt.056, camt.055, camt.029):

- **The answer is final.** HTTP 2xx, `Status` ACCP or RJCT, echoed ids equal to the delivered message (for a pacs.008, no `Id` or `TxId`), no duplicate member.
- **A redelivery gets the first answer.** The same body and key again gives the same `Status`.
- **The payment-status query returns the answer.** `messageKind` and the key give the same `Status`.

Then once:

- **Unknown reference.** The status query for a message never sent answers exactly 404, not 204 or 200.
- **Status callback and reversal.** Both are accepted with 2xx, also when redelivered.

Every message carries ids unique to the run. The kit's payments credit `CreditAccount`, so point it at a test environment.
