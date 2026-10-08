using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace IPS.Middleware.Performance;

// What one run does. The defaults are the owner's baseline (013): one minute at 10 requests per second to warm up, ten minutes at
// 50 per second measured, up to two minutes for the last callbacks, two instances and a 100 ms simulated IPS; since 013a the
// shipped timings and execution concurrency.
// Concurrency null means the shipped value; Timings Test reproduces the AppHost's fast test timings of 013; Label names the
// report files.
internal sealed record RunOptions(
    int Rate,
    TimeSpan Duration,
    int WarmUpRate,
    TimeSpan WarmUp,
    TimeSpan Drain,
    int Instances,
    TimeSpan IpsDelay,
    string Output,
    int? Concurrency = null,
    string Label = "baseline",
    string Timings = "Shipped")
{
    internal static RunOptions From(IConfiguration configuration) => new(
        int.Parse(configuration["Rate"] ?? "50", CultureInfo.InvariantCulture),
        TimeSpan.Parse(configuration["Duration"] ?? "00:10:00", CultureInfo.InvariantCulture),
        int.Parse(configuration["WarmUpRate"] ?? "10", CultureInfo.InvariantCulture),
        TimeSpan.Parse(configuration["WarmUp"] ?? "00:01:00", CultureInfo.InvariantCulture),
        TimeSpan.Parse(configuration["Drain"] ?? "00:02:00", CultureInfo.InvariantCulture),
        int.Parse(configuration["Instances"] ?? "2", CultureInfo.InvariantCulture),
        TimeSpan.Parse(configuration["IpsDelay"] ?? "00:00:00.100", CultureInfo.InvariantCulture),
        Path.GetFullPath(configuration["Output"] ?? Path.Combine(Repository.Root, "docs", "performance")),
        configuration["Concurrency"] is { } concurrency ? int.Parse(concurrency, CultureInfo.InvariantCulture) : null,
        configuration["Label"] ?? "baseline",
        configuration["Timings"] ?? "Shipped");
}

// Where the run finds the repository: the AppHost project is tests/IPS.Middleware.AppHost.
internal static class Repository
{
    internal static string Root => Path.GetFullPath(Path.Combine(Projects.IPS_Middleware_AppHost.ProjectPath, "..", ".."));

    internal static string ShippedSettings => Path.Combine(Root, "src", "IPS.Middleware.Api", "appsettings.json");
}
