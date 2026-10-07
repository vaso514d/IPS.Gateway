using System.Diagnostics;
using Xunit;

namespace IPS.Middleware.AspireTests;

// The Aspire tests need Docker for the SQL Server container. Without it they are skipped with the reason, so the rest of the
// suite still runs and the skip is visible in the result.
[AttributeUsage(AttributeTargets.Method)]
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Unavailable = new(Probe);

    public DockerFactAttribute()
    {
        Skip = Unavailable.Value;
    }

    internal static bool Available => Unavailable.Value is null;

    private static string? Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null || !process.WaitForExit(TimeSpan.FromSeconds(20)))
            {
                process?.Kill(entireProcessTree: true);
                return "Docker did not answer within 20 seconds, so the Aspire tests were skipped.";
            }

            return process.ExitCode == 0 ? null : "Docker is not running, so the Aspire tests were skipped.";
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "Docker is not installed, so the Aspire tests were skipped.";
        }
    }
}
