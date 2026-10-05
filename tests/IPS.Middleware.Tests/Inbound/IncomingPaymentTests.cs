using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using Xunit;

namespace IPS.Middleware.Tests.Inbound;

public sealed class IncomingPaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 16, 0, 0, TimeSpan.FromHours(4));
    [Fact]
    public void Registration_raises_one_event_with_normalized_participant_exact_reference_and_utc_time()
    {
        var id = Guid.NewGuid();
        var payment = IncomingPayment.Register(id, " bagage22 ", " e2E-1 ", Now);
        Assert.Equal("BAGAGE22", payment.ParticipantBic);
        Assert.Equal(" e2E-1 ", payment.EndToEndId);
        Assert.Equal(TimeSpan.Zero, payment.RegisteredAtUtc.Offset);
        Assert.Equal(Now, payment.RegisteredAtUtc);
        var registered = Assert.IsType<IncomingPaymentRegistered>(Assert.Single(payment.PendingEvents));
        Assert.Equal((id, 1, payment.RegisteredAtUtc, "BAGAGE22", " e2E-1 "),
            (registered.AggregateId, registered.Sequence, registered.OccurredAtUtc, registered.ParticipantBic, registered.EndToEndId));
        Assert.Equal(1, payment.EventSequence);
    }

    [Theory]
    [InlineData(" ", "E2E-1")]
    [InlineData("BAGAGE22", "")]
    public void Registration_requires_participant_and_reference(string participant, string endToEndId) => Assert.ThrowsAny<ArgumentException>(() => IncomingPayment.Register(Guid.NewGuid(), participant, endToEndId, Now));
    [Fact]
    public void Equal_contents_compare_numerically_by_instant_and_by_ordered_list_values()
    {
        var incoming = Incoming(Request());
        var restated = Request() with
        {
            Amount = 12.5m,
            AcceptanceDateTime = Now.ToUniversalTime(),
            PaymentInitiation = new() { ChannelCode = "WEB", Geolocation = new List<string> { "41.7", "44.8" } }
        };
        Assert.True(incoming.HasSameContents(restated));
        Assert.True(incoming.HasSameContents(incoming.Payment));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("case")]
    [InlineData("trailing")]
    [InlineData("order")]
    [InlineData("empty")]
    [InlineData("nested")]
    public void Any_changed_frozen_field_is_different_contents(string change)
    {
        var request = Request();
        var changed = change switch
        {
            "amount" => request with
            {
                Amount = 12.51m
            },
            "case" => request with
            {
                Currency = "gel"
            },
            "trailing" => request with
            {
                Debtor = request.Debtor! with
                {
                    Name = "Debtor "
                }
            },
            "order" => request with
            {
                InitiationChannelInstrument = request.InitiationChannelInstrument! with
                {
                    InstrumentCodes = ["NFC", "QR"]
                }
            },
            "empty" => request with
            {
                PaymentInitiation = request.PaymentInitiation! with
                {
                    Geolocation = []
                }
            },
            _ => request with
            {
                Remittance = request.Remittance! with
                {
                    Structured = [new()
                    {
                        ReferenceType = "MCC",
                        Reference = "5412"
                    }

                    ]
                }
            }
        };
        Assert.False(Incoming(request).HasSameContents(changed));
    }

    [Fact]
    public void Missing_list_differs_from_an_empty_list()
    {
        var withoutLocation = Request() with
        {
            PaymentInitiation = new()
            {
                ChannelCode = "WEB"
            }
        };
        Assert.False(Incoming(withoutLocation).HasSameContents(withoutLocation with { PaymentInitiation = new() { ChannelCode = "WEB", Geolocation = [] } }));
    }

    private static IncomingPacs008 Incoming(Pacs008Request request) => new(request, new("header", "group", request.EndToEndId!, null, null, null, null, null, null, null));
    private static Pacs008Request Request() => new()
    {
        EndToEndId = "E2E-1",
        AcceptanceDateTime = Now,
        Amount = 12.50m,
        Currency = "GEL",
        Debtor = new() { Type = 1, Name = "Debtor", Address = new() { TownName = "Tbilisi" } },
        PaymentInitiation = new() { ChannelCode = "WEB", Geolocation = ["41.7", "44.8"] },
        InitiationChannelInstrument = new() { ChannelCode = "MOB", InstrumentCodes = ["QR", "NFC"] },
        Remittance = new() { Structured = [new() { ReferenceType = "MCC", Reference = "5411" }] }
    };
}
