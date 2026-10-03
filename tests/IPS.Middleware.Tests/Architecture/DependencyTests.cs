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
    [InlineData("IPS.Middleware.Domain")]
    [InlineData("IPS.Middleware.Application")]
    public void Core_projects_depend_only_on_the_base_class_library(string project)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Architecture", project + ".dependencies.txt"));
        Assert.Empty(Values(lines, "packageReference"));
        Assert.Empty(Values(lines, "assemblyReference"));
        Assert.All(Values(lines, "frameworkReference"), framework => Assert.Equal("Microsoft.NETCore.App", framework));
    }

    private static IEnumerable<string> Values(IEnumerable<string> lines, string key) =>
        lines.Where(line => line.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(line => line[(key.Length + 1)..]);
}
