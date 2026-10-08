using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingCoreReplyTests
{
    [Theory]
    [InlineData("{}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\"}", CoreOutcome.Accepted)]
    [InlineData("{\"Status\":\"RJCT\",\"EndToEndId\":\"OTHER\"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":null}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":42}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"PDNG\"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"Status\":\"RJCT\"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"Id\":\"OTHER\"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"TxId\":\"OTHER\"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"EndToEndId\":\"E2E \"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"ProcessedAtUtc\":\"invalid\"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"InternalErrorCode\":\"wrong-type\"}", CoreOutcome.Unknown)]
    [InlineData("[]", CoreOutcome.Unknown)]
    [InlineData("malformed", CoreOutcome.Unknown)]
    [InlineData("{\"status\":\"accp\",\"EndToEndId\":\"E2E\"}", CoreOutcome.Accepted)]
    [InlineData("{\"Status\":\"ACCP\",\"Other\":1,\"other\":2}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"Other\":1,\"Other\":2}", CoreOutcome.Unknown)]
    [InlineData("null", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"status\":\"ACCP\"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"TXID\":\"TX\",\"TxId\":\"TX\"}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"Extra\":{\"a\":1,\"a\":2}}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"Description\":5}", CoreOutcome.Unknown)]
    [InlineData("{\"Status\":\"ACCP\",\"Extra\":{\"a\":1,\"A\":2}}", CoreOutcome.Accepted)]
    public void Requires_explicit_status_but_allows_missing_reference(string json, CoreOutcome expected)
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var result = new IncomingCoreReplyInterpreter().Interpret(new(new(200, json), null, now),
            new("HEADER", "GROUP", "E2E", "TX", null, null, null, null, null, null));
        Assert.Equal(expected, result.Status);
        Assert.Equal(now, result.ProcessedAtUtc);
    }
    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(302)]
    public void Http_failure_does_not_confirm_a_credit(int status)
    {
        var at = DateTimeOffset.UnixEpoch;
        var result = new IncomingCoreReplyInterpreter().Interpret(new(new(status, "{\"Status\":\"ACCP\"}"), null, at),
            new("HEADER", "GROUP", "E2E", null, null, null, null, null, null, null));
        Assert.Equal(CoreOutcome.Unknown, result.Status);
    }

    [Fact]
    public void Reported_processing_time_is_kept_as_the_same_utc_instant()
    {
        var result = new IncomingCoreReplyInterpreter().Interpret(
            new(new(200, "{\"Status\":\"RJCT\",\"ProcessedAtUtc\":\"2026-10-04T16:00:00+04:00\",\"ReasonCode\":\"AC01\",\"InternalErrorCode\":7}"),
                null, DateTimeOffset.UnixEpoch),
            new("HEADER", "GROUP", "E2E", "TX", null, null, null, null, null, null));
        Assert.Equal(new CorePaymentResult(CoreOutcome.Rejected, new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero), null, "AC01", 7), result);
        Assert.Equal(TimeSpan.Zero, result.ProcessedAtUtc.Offset);
    }
}
