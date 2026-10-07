using System.Diagnostics;

namespace IPS.Middleware.AppHost;

// Publishes the API on the host and puts the Dockerfile next to the output, which is the build context of the image.
internal static class ApiImage
{
    internal static string Prepare(string appHostDirectory)
    {
        var repository = Path.GetFullPath(Path.Combine(appHostDirectory, "..", ".."));
        var project = Path.Combine(repository, "src", "IPS.Middleware.Api", "IPS.Middleware.Api.csproj");
        var output = Path.Combine(Path.GetTempPath(), "ips-aspire-image-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "publish", project, "--configuration", "Release", "--output", output })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet publish.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            Directory.Delete(output, recursive: true);
            throw new InvalidOperationException("dotnet publish of the API did not finish in 5 minutes.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet publish of the API failed:{Environment.NewLine}{standardOutput.Result}{standardError.Result}");
        }

        File.Copy(Path.Combine(repository, "src", "IPS.Middleware.Api", "Dockerfile"), Path.Combine(output, "Dockerfile"), overwrite: true);
        return output;
    }
}
