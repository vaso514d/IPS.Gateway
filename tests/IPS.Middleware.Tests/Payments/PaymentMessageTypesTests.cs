using IPS.Middleware.Application.Payments;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class PaymentMessageTypesTests
{
    [Theory]
    [InlineData("pain.001", true)]
    [InlineData("pain.001.001.12", true)]
    [InlineData("pain.001.001.012", true)]
    [InlineData("pain.002", false)]
    [InlineData("pain.001.001.11", false)]
    public void A_payment_initiation_is_named_by_its_short_type_or_either_definition(string messageType, bool expected)
    {
        Assert.Equal(expected, PaymentMessageTypes.IsPain001(messageType));
    }

    [Theory]
    [InlineData("pacs.009", true)]
    [InlineData("pacs.004.001.13", true)]
    [InlineData("pain.001", true)]
    [InlineData("pacs.008", false)]
    [InlineData("pacs.002", false)]
    [InlineData("camt.056", true)]
    [InlineData("camt.029.001.13", true)]
    [InlineData("camt.055.001.012", true)]
    [InlineData("camt.055.001.08", true)]
    [InlineData("camt.053", false)]
    public void Transfers_recalls_refusals_and_cancellations_are_handed_to_the_core_as_transfers(string messageType, bool expected)
    {
        Assert.Equal(expected, PaymentMessageTypes.IsIncomingTransfer(messageType));
    }
}
