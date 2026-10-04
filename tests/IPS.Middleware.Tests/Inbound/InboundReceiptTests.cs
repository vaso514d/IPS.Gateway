using IPS.Middleware.Application.Inbound;
using Xunit;

namespace IPS.Middleware.Tests.Inbound;

public sealed class InboundReceiptTests
{
    [Fact]
    public void Envelope_normalizes_identity_and_time_but_preserves_untrusted_xml()
    {
        const string xml = "  <broken>\r\n";
        var time = new DateTimeOffset(2026, 10, 4, 14, 0, 0, TimeSpan.FromHours(4));
        var receipt = new InboundReceipt(" itsbge22 ", -3, " pacs.008 ", xml, true, time);
        Assert.Equal("ITSBGE22", receipt.ParticipantBic);
        Assert.Equal("pacs.008", receipt.MessageType);
        Assert.Equal(xml, receipt.RawXml);
        Assert.Equal(-3, receipt.Sequence);
        Assert.True(receipt.PossibleDuplicate);
        Assert.Equal(TimeSpan.Zero, receipt.ReceivedAtUtc.Offset);
        Assert.Equal(time.UtcDateTime, receipt.ReceivedAtUtc.UtcDateTime);
    }

    [Theory]
    [InlineData("", "pacs.008")]
    [InlineData("TOO-LONG-BIC-1", "pacs.008")]
    [InlineData("ITSBGE22", " ")]
    public void Invalid_storage_envelopes_are_rejected(string bic, string type) =>
        Assert.ThrowsAny<ArgumentException>(() => new InboundReceipt(bic, 1, type, "", false, DateTimeOffset.UtcNow));
}
