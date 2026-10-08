using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace IPS.Middleware.Performance;

// What the run measured on: one machine runs the containers, the API instances, the simulators and the generator. Containers lists
// every container running at the start of the load, the run's own and anyone else's.
internal sealed record Machine(
    string Cpu,
    int LogicalProcessors,
    double MemoryGiB,
    string OperatingSystem,
    string Runtime,
    string DockerVersion,
    int DockerCpus,
    double DockerMemoryGiB,
    IReadOnlyList<string> Containers)
{
    private const double GiB = 1024 * 1024 * 1024;

    internal static async Task<Machine> DescribeAsync(CancellationToken cancellationToken)
    {
        var docker = (await DockerAsync(["info", "--format", "{{.ServerVersion}}|{{.NCPU}}|{{.MemTotal}}"], cancellationToken)).Split('|');
        var containers = await DockerAsync(["ps", "--format", "{{.Names}} ({{.Image}})"], cancellationToken);
        return new(
            CpuModel(),
            Environment.ProcessorCount,
            Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / GiB, 1),
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            docker[0],
            int.Parse(docker[1], CultureInfo.InvariantCulture),
            Math.Round(long.Parse(docker[2], CultureInfo.InvariantCulture) / GiB, 1),
            containers
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private static string CpuModel()
    {
        if (System.OperatingSystem.IsWindows())
        {
            return Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) is string name
                ? name.Trim()
                : RuntimeInformation.ProcessArchitecture.ToString();
        }

        var model = File.Exists("/proc/cpuinfo")
            ? File.ReadLines("/proc/cpuinfo").FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal))
            : null;
        return model is null ? RuntimeInformation.ProcessArchitecture.ToString() : model[(model.IndexOf(':') + 1)..].Trim();
    }

    // Docker's view, which on Windows is Docker Desktop's VM, not the host. Both outputs are read while the command runs, so a full
    // pipe cannot stall it, and a cancelled run does not leave it behind.
    private static async Task<string> DockerAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start docker.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return process.ExitCode == 0
            ? (await output).Trim()
            : throw new InvalidOperationException($"docker {string.Join(' ', arguments)} failed: {await error}");
    }
}
