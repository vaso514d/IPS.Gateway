using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using Xunit;

namespace IPS.Middleware.Tests.Inbound;

public sealed class IncomingPacs008SnapshotTests
{
    [Fact]
    public void Snapshot_copies_caller_owned_collections_without_applying_outgoing_validation()
    {
        var locations = new[] { "original location" };
        var instruments = new[] { "original instrument" };
        var references = new[] { new Pacs008StructuredRemittanceInput { Reference = "original reference" } };
        var input = new Pacs008Request
        {
            PaymentInitiation = new() { Geolocation = locations },
            InitiationChannelInstrument = new() { InstrumentCodes = instruments },
            Remittance = new() { Structured = references }
        };
        var snapshot = new IncomingPacs008(input, new("header", "group", "end", null, null, null, null, null, null, null));
        locations[0] = "changed";
        instruments[0] = "changed";
        references[0] = new()
        {
            Reference = "changed"
        };
        Assert.Equal("original location", snapshot.Payment.PaymentInitiation!.Geolocation![0]);
        Assert.Equal("original instrument", snapshot.Payment.InitiationChannelInstrument!.InstrumentCodes![0]);
        Assert.Equal("original reference", snapshot.Payment.Remittance!.Structured![0].Reference);
    }
}
