using Microsoft.Extensions.Hosting;

namespace IPS.Middleware.AppHost;

// The run's throwaway files (generated certificates, the published API) are removed when the AppHost stops.
internal sealed class TemporaryDirectories : IHostedService
{
    private readonly List<string> _directories = [];

    internal void Add(string directory) => _directories.Add(directory);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var directory in _directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A container may still hold a mount for a moment; the files are throwaway.
            }
        }

        return Task.CompletedTask;
    }
}
