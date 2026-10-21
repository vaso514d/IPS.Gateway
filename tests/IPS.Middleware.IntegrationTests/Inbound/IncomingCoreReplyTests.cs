using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

// The core system's answer to an incoming pacs.008 (specification 022): status ACCEPTED or REJECTED, echoing at most the
// payment's transactionId.
public sealed class IncomingCoreReplyTests
{
    private const string TransactionId = "11111111-2222-3333-4444-555555555555";

    [Theory]
    [InlineData("{}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCEPTED\"}", CoreOutcome.Accepted)]
    [InlineData("{\"status\":\"REJECTED\",\"transactionId\":\"OTHER\"}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":null}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":42}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"PENDING\"}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCP\"}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCEPTED\",\"status\":\"REJECTED\"}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCEPTED\",\"transactionId\":\"" + TransactionId + " \"}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCEPTED\",\"processedAtUtc\":\"invalid\"}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCEPTED\",\"reasonDescription\":5}", CoreOutcome.Unknown)]
    [InlineData("[]", CoreOutcome.Unknown)]
    [InlineData("malformed", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"accepted\",\"TransactionId\":\"" + TransactionId + "\"}", CoreOutcome.Accepted)]
    [InlineData("{\"status\":\"ACCEPTED\",\"Other\":1,\"other\":2}", CoreOutcome.Unknown)]
    [InlineData("null", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCEPTED\",\"Status\":\"ACCEPTED\"}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCEPTED\",\"Extra\":{\"a\":1,\"a\":2}}", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"ACCEPTED\",\"Extra\":{\"a\":1,\"A\":2}}", CoreOutcome.Accepted)]
    public void Requires_explicit_status_but_allows_missing_transaction_id(string json, CoreOutcome expected)
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var result = new IncomingCoreReplyInterpreter().Interpret(new(new(200, json), null, now), Payment());
        Assert.Equal(expected, result.Status);
        Assert.Equal(now, result.ProcessedAtUtc);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(302)]
    public void Http_failure_does_not_confirm_a_credit(int status)
    {
        var result = new IncomingCoreReplyInterpreter().Interpret(new(new(status, "{\"status\":\"ACCEPTED\"}"), null, DateTimeOffset.UnixEpoch), Payment());
        Assert.Equal(CoreOutcome.Unknown, result.Status);
    }

    [Fact]
    public void A_rejection_keeps_its_reason_and_an_acceptance_its_posting()
    {
        var at = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var rejected = new IncomingCoreReplyInterpreter().Interpret(
            new(new(200, "{\"transactionId\":\"" + TransactionId + "\",\"status\":\"REJECTED\",\"reasonCode\":\"AC04\",\"reasonDescription\":\"Account closed\"}"), null, at),
            Payment());
        var accepted = new IncomingCoreReplyInterpreter().Interpret(
            new(new(200, "{\"status\":\"ACCEPTED\",\"coreReference\":\"POSTING-1\"}"), null, at), Payment());

        Assert.Equal(new CorePaymentResult(CoreOutcome.Rejected, at, null, "AC04", null, "Account closed"), rejected);
        Assert.Equal(new CorePaymentResult(CoreOutcome.Accepted, at, "POSTING-1"), accepted);
    }

    private static IncomingPayment Payment() =>
        IncomingPayment.Register(Guid.Parse(TransactionId), "BAGAGE22", "E2E", DateTimeOffset.UnixEpoch);
}
