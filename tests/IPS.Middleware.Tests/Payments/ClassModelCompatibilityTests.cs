using System.Text.Json;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Domain.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class ClassModelCompatibilityTests
{
    [Fact]
    public void Callback_keys_coalesce_by_payment_and_outcome_sequence()
    {
        var paymentId = Guid.NewGuid();
        var first = new StatusDeliveryKey(paymentId, 2);
        var same = new StatusDeliveryKey(paymentId, 2);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        var keys = new HashSet<StatusDeliveryKey> { first, same, new(paymentId, 3) };
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void Frozen_payment_contents_compare_lists_by_order_and_timestamps_by_instant()
    {
        var coordinates = new List<string> { "1", "2" };
        var request = new Pacs008Request
        {
            EndToEndId = "payment",
            CreationDateTime = DateTimeOffset.Parse("2026-10-05T10:00:00Z"),
            PaymentInitiation = new() { Geolocation = coordinates },
            Remittance = new() { Structured = [new() { Reference = "original" }] }
        };
        var original = new IncomingPacs008Reference("business", "group", "payment", null, null, null, null, null, null, null);
        var frozen = new IncomingPacs008(request, original);
        var restored = JsonSerializer.Deserialize<Pacs008Request>(JsonSerializer.Serialize(request))!;
        Assert.True(frozen.HasSameContents(restored));
        Assert.True(frozen.HasSameContents(restored with
        {
            CreationDateTime = restored.CreationDateTime!.Value.ToOffset(TimeSpan.FromHours(4))
        }));
        coordinates[0] = "changed";
        Assert.False(frozen.HasSameContents(request));
        Assert.True(frozen.HasSameContents(restored));
        Assert.False(frozen.HasSameContents(restored with
        {
            PaymentInitiation = new() { Geolocation = ["2", "1"] }
        }));
        Assert.False(frozen.HasSameContents(restored with
        {
            Remittance = new() { Structured = [new() { Reference = "changed" }] }
        }));
    }

    [Fact]
    public void Existing_event_payload_deserializes_into_explicit_event_properties()
    {
        const string json = """
            {"eventId":"11111111-1111-1111-1111-111111111111","aggregateId":"22222222-2222-2222-2222-222222222222","sequence":1,"occurredAtUtc":"2026-10-05T10:00:00+00:00","messageType":"pacs.008","clientReference":"original"}
            """;
        var restored = JsonSerializer.Deserialize<PaymentReceived>(json, JsonSerializerOptions.Web)!;
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), restored.EventId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), restored.AggregateId);
        Assert.Equal(1, restored.Sequence);
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T10:00:00Z"), restored.OccurredAtUtc);
        Assert.Equal("pacs.008", restored.MessageType);
        Assert.Equal("original", restored.ClientReference);
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(json).RootElement,
            JsonSerializer.SerializeToElement(restored, JsonSerializerOptions.Web)));
    }
}
