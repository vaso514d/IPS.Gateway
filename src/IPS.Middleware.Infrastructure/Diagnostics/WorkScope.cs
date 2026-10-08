using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Diagnostics;

// Every log line written while one unit of work runs carries what identifies it, so one query follows it across the request,
// the worker and recovery.
internal static class WorkScope
{
    internal static IDisposable? Begin(ILogger logger, string workflow, object? key, params (string Name, object? Value)[] more)
    {
        var state = new Dictionary<string, object?> { ["Workflow"] = workflow, ["WorkKey"] = key?.ToString() };
        foreach (var (name, value) in more)
        {
            state[name] = value;
        }

        return logger.BeginScope(state);
    }
}
