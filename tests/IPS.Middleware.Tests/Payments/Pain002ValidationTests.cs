using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pain002;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class Pain002ValidationTests
{
    private static readonly Pacs008Policy Policy = new("BAGAGE22", null, [new("GEL")]);

    private static Pain002Request Valid() => new()
    {
        ClientReference = "ref-1",
        Id = "REF-1",
        OriginalMessageId = "PAIN001-MSG-1",
        OriginalPaymentInformationId = "PMTINF-1",
        ReasonCode = "CUST"
    };

    [Fact]
    public void A_valid_request_is_normalized_and_optional_text_is_absent_when_blank()
    {
        var result = ValidatedPain002.Validate(Valid() with
        {
            ClientReference = " ref-1 ",
            AdditionalInformation = "   ",
            OriginatorName = " Payer Bank ",
            CreatedAt = new DateTimeOffset(2026, 10, 4, 14, 0, 0, TimeSpan.FromHours(4))
        }, Policy);

        var payment = Assert.IsType<ValidatedPain002>(result.Payment);
        Assert.Empty(result.Errors);
        Assert.Equal(("ref-1", "REF-1", "BAGAGE22"), (payment.ClientReference, payment.MessageId, payment.ParticipantBic));
        Assert.Null(payment.AdditionalInformation);
        Assert.Equal("Payer Bank", payment.OriginatorName);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero), payment.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, payment.CreatedAtUtc!.Value.Offset);
    }

    public static TheoryData<string, Func<Pain002Request, Pain002Request>> Invalid() => new()
    {
        { "clientReference", r => r with { ClientReference = null } },
        { "clientReference", r => r with { ClientReference = new string('x', 36) } },
        { "id", r => r with { Id = null } },
        { "id", r => r with { Id = new string('x', 36) } },
        { "originalMessageId", r => r with { OriginalMessageId = " " } },
        { "originalPaymentInformationId", r => r with { OriginalPaymentInformationId = null } },
        { "originalPaymentInformationId", r => r with { OriginalPaymentInformationId = new string('x', 36) } },
        { "reasonCode", r => r with { ReasonCode = null } },
        { "reasonCode", r => r with { ReasonCode = "TOOLONG" } },
        { "reasonCode", r => r with { ReasonCode = "C\u0001ST" } },
        { "additionalInformation", r => r with { AdditionalInformation = new string('x', 106) } },
        { "originatorName", r => r with { OriginatorName = new string('x', 141) } }
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_request_is_reported_at_the_failing_field(string field, Func<Pain002Request, Pain002Request> break_)
    {
        var result = ValidatedPain002.Validate(break_(Valid()), Policy);

        Assert.Null(result.Payment);
        Assert.Contains(result.Errors, error => error.Field == field);
    }
}
