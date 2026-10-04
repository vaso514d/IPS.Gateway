using IPS.Middleware.Application.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Transactions;

public sealed class IntakeValidationTests
{
    [Theory]
    [InlineData(null, "ref", "{}", "messageType")]
    [InlineData(" ", "ref", "{}", "messageType")]
    [InlineData("pacs.008", null, "{}", "clientReference")]
    [InlineData("pacs.008", " ", "{}", "clientReference")]
    [InlineData("pacs.008", "ref", null, "requestJson")]
    [InlineData("pacs.008", "ref", " ", "requestJson")]
    public void Invalid_input_cannot_produce_an_intake_request(string? type, string? reference, string? payload, string field)
    {
        var result = ValidatedIntakeRequest.Validate(type, reference, payload);
        Assert.Null(result.Request);
        Assert.Equal(field, Assert.Single(result.Errors).Field);
    }

    [Theory]
    [InlineData(17, 1, "messageType")]
    [InlineData(1, 36, "clientReference")]
    public void Storage_identifier_limits_are_checked_before_intake(int typeLength, int referenceLength, string field)
    {
        var result = ValidatedIntakeRequest.Validate(new string('a', typeLength), new string('b', referenceLength), "{}");
        Assert.Null(result.Request);
        Assert.Equal(field, Assert.Single(result.Errors).Field);
    }

    [Fact]
    public void Validation_collects_field_errors()
    {
        var result = ValidatedIntakeRequest.Validate(null, null, null);
        Assert.Null(result.Request);
        Assert.Equal(new[] { "messageType", "clientReference", "requestJson" }, result.Errors.Select(e => e.Field));
    }

    [Fact]
    public void Valid_input_is_normalized_once_and_payload_is_preserved()
    {
        const string json = " { \"name\": \"საქართველო\" } ";
        var result = ValidatedIntakeRequest.Validate(" pacs.008 ", " reference ", json);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Request);
        Assert.Equal("pacs.008", result.Request.MessageType);
        Assert.Equal("reference", result.Request.ClientReference);
        Assert.Equal(json, result.Request.RequestJson);
        Assert.NotNull(ValidatedIntakeRequest.Validate(new string('a', 16), new string('b', 35), "{}").Request);
    }
}
