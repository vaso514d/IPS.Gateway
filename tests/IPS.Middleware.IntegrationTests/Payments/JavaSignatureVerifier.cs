using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

internal static class JavaSignatureVerifier
{
    internal static async Task<bool> VerifyAsync(string xml, X509Certificate2 certificate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "IPS-SignatureTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var messagePath = Path.Combine(directory, "message.xml");
        var certificatePath = Path.Combine(directory, "certificate.cer");
        try
        {
            await File.WriteAllTextAsync(messagePath, xml);
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Cert));
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            var executable = string.IsNullOrWhiteSpace(javaHome) ? "java" :
                Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Payments", "Fixtures", "VerifyXmlSignature.java"));
            start.ArgumentList.Add(messagePath);
            start.ArgumentList.Add(certificatePath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Java. Signing tests require JDK17+.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            Assert.True(process.ExitCode == 0, await error);
            var result = (await output).Trim();
            Assert.Contains(result, new[] { "VALID", "INVALID" });
            return result == "VALID";
        }
        finally
        {
            File.Delete(messagePath);
            File.Delete(certificatePath);
            Directory.Delete(directory);
        }
    }
}
