using Microsoft.Data.SqlClient;

namespace IPS.Middleware.Performance;

// The most SQL sessions one client process held open on the run's database. The orchestrator reports the process of `dotnet
// run`, not the API process it starts, so the processes are named by their id only.
internal sealed record ConnectionPeak(string Process, int Peak);

// Open SQL sessions per client process, sampled while the run loads the service (--Diagnose). A pooled connection stays open
// while idle in its pool, so the peak of an API process is the most connections its pool held, against Max Pool Size (100 by
// default). The harness's own sessions are left out.
internal static class Connections
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    internal static async Task<IReadOnlyDictionary<int, int>> SampleAsync(string connectionString, CancellationToken stop)
    {
        var peaks = new Dictionary<int, int>();
        using var timer = new PeriodicTimer(Interval);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(stop);
            do
            {
                await using var command = new SqlCommand(
                    "SELECT host_process_id, COUNT(*) FROM sys.dm_exec_sessions " +
                    "WHERE is_user_process = 1 AND database_id = DB_ID() AND host_process_id <> @harness GROUP BY host_process_id",
                    connection);
                command.Parameters.AddWithValue("@harness", Environment.ProcessId);
                await using var reader = await command.ExecuteReaderAsync(stop);
                while (await reader.ReadAsync(stop))
                {
                    var process = reader.GetInt32(0);
                    peaks[process] = Math.Max(peaks.GetValueOrDefault(process), reader.GetInt32(1));
                }
            }
            while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The run is over.
        }

        return peaks;
    }

    internal static IReadOnlyList<ConnectionPeak> Peaks(IReadOnlyDictionary<int, int> peaks) => peaks
        .OrderBy(peak => peak.Key)
        .Select(peak => new ConnectionPeak($"process {peak.Key}", peak.Value))
        .ToArray();
}
