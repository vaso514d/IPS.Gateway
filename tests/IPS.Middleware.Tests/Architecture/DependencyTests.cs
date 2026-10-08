using Xunit;

namespace IPS.Middleware.Tests.Architecture;

public sealed class DependencyTests
{
    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["IPS.MiidleWear.Contracts"] = [],
        ["IPS.Middleware.Domain"] = [],
        ["IPS.Middleware.Application"] = ["IPS.Middleware.Domain"],
        ["IPS.Middleware.Infrastructure"] =
            ["IPS.Middleware.Application", "IPS.Middleware.Domain", "IPS.MiidleWear.Contracts"],
        ["IPS.Middleware.Api"] =
            ["IPS.Middleware.Application", "IPS.Middleware.Infrastructure", "IPS.MiidleWear.Contracts"]
    };

    [Fact]
    public void Evaluated_project_references_follow_the_layer_rules()
    {
        var manifests = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Architecture"), "*.dependencies.txt");
        Assert.Equal(AllowedReferences.Count, manifests.Length);

        foreach (var file in manifests)
        {
            var lines = File.ReadAllLines(file);
            var project = Assert.Single(Values(lines, "project"));
            Assert.True(AllowedReferences.TryGetValue(project, out var allowed), $"Unknown production project: {project}");
            Assert.Equal(allowed!.Order(StringComparer.Ordinal), Values(lines, "projectReference").Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("IPS.MiidleWear.Contracts")]
    [InlineData("IPS.Middleware.Application")]
    public void Contracts_remains_isolated_and_Application_allows_only_FluentValidation(string project)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Architecture", project + ".dependencies.txt"));
        Assert.Equal(project == "IPS.Middleware.Application" ? new[] { "FluentValidation" } : [], Values(lines, "packageReference"));
        Assert.Equal(project == "IPS.Middleware.Application" ? new[] { "FluentValidation", "Stateless" } : [], Values(lines, "assemblyReference").Order(StringComparer.Ordinal));
        Assert.All(Values(lines, "frameworkReference"), framework => Assert.Equal("Microsoft.NETCore.App", framework));
    }

    [Fact]
    public void Domain_allows_only_Stateless_beyond_the_base_class_library()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Architecture", "IPS.Middleware.Domain.dependencies.txt"));
        Assert.Equal(new[] { "Stateless" }, Values(lines, "packageReference"));
        Assert.Equal(new[] { "Stateless" }, Values(lines, "assemblyReference"));
        Assert.All(Values(lines, "frameworkReference"), framework => Assert.Equal("Microsoft.NETCore.App", framework));
    }

    private static IEnumerable<string> Values(IEnumerable<string> lines, string key) =>
        lines.Where(line => line.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(line => line[(key.Length + 1)..]);
}
