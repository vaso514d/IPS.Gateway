using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace IPS.Middleware.Performance;

// A setting at least one instance ran with that is not the shipped value. Shipped is null when appsettings.json does not set it;
// UsedByInstance lists the value of middleware-1, middleware-2 and so on.
internal sealed record SettingDifference(string Key, string? Shipped, IReadOnlyList<string?> UsedByInstance);

// Compares the environment the AppHost gave each instance with the shipped appsettings.json. Only service settings count: keys
// under a section of appsettings.json, connection strings and the host environment, not the orchestrator's own variables
// (ports, telemetry endpoints).
internal static class ServiceSettings
{
    internal static IReadOnlyDictionary<string, string?> Shipped() => new ConfigurationBuilder()
        .AddJsonFile(Repository.ShippedSettings)
        .Build()
        .AsEnumerable()
        .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<SettingDifference> Differences(
        IReadOnlyDictionary<string, string?> shipped,
        IReadOnlyList<IReadOnlyList<EnvironmentVariableSnapshot>> instances)
    {
        var sections = shipped.Keys
            .Select(key => key.Split(':')[0])
            .Append("ConnectionStrings")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = instances
            .Select(environment => environment
                .Select(variable => (Key: variable.Name.Replace("__", ":", StringComparison.Ordinal), variable.Value))
                .Where(setting => sections.Contains(setting.Key.Split(':')[0]) || setting.Key == "ASPNETCORE_ENVIRONMENT")
                .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return used
            .SelectMany(settings => settings)
            .Where(setting => !string.Equals(shipped.GetValueOrDefault(setting.Key), setting.Value, StringComparison.OrdinalIgnoreCase))
            .Select(setting => setting.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .Select(key => new SettingDifference(
                key,
                shipped.GetValueOrDefault(key),
                used
                    .Select(settings => Shown(key, settings.GetValueOrDefault(key)))
                    .ToArray()))
            .ToArray();
    }

    // The connection string and the certificate passwords are generated for the run, and the run's files live under the user's
    // temp directory (the AppHost writes those paths with forward slashes).
    private static string? Shown(string key, string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
        {
            return "(the AppHost's SQL Server container)";
        }

        if (key.EndsWith(":Password", StringComparison.OrdinalIgnoreCase))
        {
            return "(generated)";
        }

        var temporary = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        return value
            .Replace(temporary, "%TEMP%", StringComparison.OrdinalIgnoreCase)
            .Replace(temporary.Replace('\\', '/'), "%TEMP%", StringComparison.OrdinalIgnoreCase);
    }
}
