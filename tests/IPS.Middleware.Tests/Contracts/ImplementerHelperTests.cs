using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using IPS.MiidleWear.Contracts.Abstractions;
using IPS.MiidleWear.Contracts.Http;
using IPS.MiidleWear.Contracts.Pacs008;
using Xunit;

namespace IPS.Middleware.Tests.Contracts;

// The Contracts additions for core-system implementers (017b): the gateway's JSON conventions, the PDNG answer and the
// documentation every public DTO property carries into IntelliSense and the OpenAPI schemas.
public sealed class ImplementerHelperTests
{
    private static readonly Assembly Contracts = typeof(IGatewayApi).Assembly;

    [Theory]
    [MemberData(nameof(CompatibilityTests.WireCases), MemberType = typeof(CompatibilityTests))]
    public void The_shared_json_options_read_and_write_every_wire_case_as_the_contract_does(
        string name, string typeName, string input, string expected)
    {
        var type = Contracts.GetType(typeName, throwOnError: true)!;

        var value = JsonSerializer.Deserialize(input, type, IpsJson.SerializerOptions);

        Assert.True(expected == JsonSerializer.Serialize(value, type, IpsJson.SerializerOptions), $"Wire shape differs: {name}");
    }

    [Fact]
    public void The_shared_json_options_cannot_be_changed()
    {
        Assert.True(IpsJson.SerializerOptions.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => IpsJson.SerializerOptions.WriteIndented = true);
    }

    [Fact]
    public void A_pending_answer_is_pdng_and_not_accepted()
    {
        var pending = Pacs008PaymentResultDto.Pending("MSG-1", "E2E-1", "TX-1", "Posting in progress");

        Assert.Equal((Pacs008PaymentStatuses.Pending, false), (pending.Status, pending.Accepted));
        Assert.Equal("""{"Id":"MSG-1","EndToEndId":"E2E-1","TxId":"TX-1","Status":"PDNG","Accepted":false,"Description":"Posting in progress","ProcessedAtUtc":"2026-01-01T00:00:00+00:00"}""",
            JsonSerializer.Serialize(pending with { ProcessedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }, IpsJson.SerializerOptions));
    }

    [Fact]
    public void Every_public_property_of_the_contract_is_documented()
    {
        var documentation = XDocument.Load(Path.ChangeExtension(Contracts.Location, ".xml"));
        var documented = documentation.Descendants("member")
            .Where(member => member.Element("summary") is { } summary && !string.IsNullOrWhiteSpace(summary.Value))
            .Select(member => member.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);

        var undocumented = Contracts.GetExportedTypes()
            .Where(type => !type.IsEnum && !type.IsInterface && type.Namespace != "IPS.MiidleWear.Contracts.Routing")
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.Name != "EqualityContract")
                .Select(property => $"P:{type.FullName}.{property.Name}"))
            .Where(member => !documented.Contains(member))
            .ToArray();

        Assert.Empty(undocumented);
    }
}
