using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.MiidleWear.Contracts.Abstractions;
using Xunit;

namespace IPS.Middleware.Tests.Contracts;

public sealed class CompatibilityTests
{
    private static readonly Assembly Contracts = typeof(IGatewayApi).Assembly;
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [Fact]
    public void Public_types_members_and_route_metadata_match_the_original_assembly()
    {
        var expected = File.ReadAllText(BaselinePath("public-api.json")).TrimEnd().ReplaceLineEndings("\n");
        Assert.Equal(expected, ContractSnapshot.Create(Contracts));
    }

    [Fact]
    public void Imported_contract_sources_match_the_original_git_blobs()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(BaselinePath("source-manifest.json")));
        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "ContractSources");
        var entries = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
        var expectedFiles = entries.Select(entry => entry.GetProperty("path").GetString()!).Order(StringComparer.Ordinal);
        var actualFiles = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/')).Order(StringComparer.Ordinal);
        Assert.Equal(expectedFiles, actualFiles);

        foreach (var entry in entries)
        {
            var relativePath = entry.GetProperty("path").GetString()!;
            var content = File.ReadAllBytes(Path.Combine(sourceDirectory, relativePath));
            var header = Encoding.UTF8.GetBytes($"blob {content.Length}\0");
            var gitObject = new byte[header.Length + content.Length];
            header.CopyTo(gitObject, 0);
            content.CopyTo(gitObject, header.Length);
            var actualBlob = Convert.ToHexString(SHA1.HashData(gitObject)).ToLowerInvariant();
            Assert.True(entry.GetProperty("gitBlob").GetString() == actualBlob, $"Contract source changed: {relativePath}");
        }
    }

    [Fact]
    public void Contracts_reference_only_the_base_class_library()
    {
        Assert.All(Contracts.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name!.StartsWith("System", StringComparison.Ordinal) || reference.Name == "netstandard",
                $"Contracts gained a dependency: {reference.Name}"));
    }

    public static IEnumerable<object[]> WireCases()
    {
        using var fixtures = JsonDocument.Parse(File.ReadAllText(BaselinePath("wire-cases.json")));
        return fixtures.RootElement.EnumerateArray().Select(item => new object[]
        {
            item.GetProperty("name").GetString()!,
            item.GetProperty("type").GetString()!,
            item.GetProperty("input").GetRawText(),
            item.GetProperty("expected").GetString()!
        }).ToArray();
    }

    [Theory]
    [MemberData(nameof(WireCases))]
    public void Representative_json_matches_the_original_contract(
        string name, string typeName, string input, string expected)
    {
        var type = Contracts.GetType(typeName, throwOnError: true)!;
        var value = JsonSerializer.Deserialize(input, type, WireOptions);
        Assert.NotNull(value);
        Assert.True(expected == JsonSerializer.Serialize(value, type, WireOptions), $"Wire shape changed: {name}");
    }

    private static string BaselinePath(string file) => Path.Combine(AppContext.BaseDirectory, "Baselines", file);
}
