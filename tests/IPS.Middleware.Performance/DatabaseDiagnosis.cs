using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace IPS.Middleware.Performance;

// One statement as Query Store recorded it over the run: executions (failed ones are the aborted or exception executions, a
// command timeout among them), durations in milliseconds, the time it waited by category, its text and a summary of the plan
// it ran with most often.
internal sealed record QueryCost(
    long QueryId,
    long Executions,
    long Failed,
    double TotalMilliseconds,
    double MeanMilliseconds,
    double MaxMilliseconds,
    double CpuMilliseconds,
    double MeanLogicalReads,
    IReadOnlyDictionary<string, double> WaitMilliseconds,
    string Text,
    string Plan);

internal sealed record DeadlockProcess(string Id, bool Victim, string LockMode, string WaitResource, string Statement);

internal sealed record DeadlockResource(string Kind, string Object, string Index, string Owners, string Waiters);

// A deadlock graph from the system_health session; Graph is the whole xml_deadlock_report.
internal sealed record Deadlock(DateTimeOffset AtUtc, IReadOnlyList<DeadlockProcess> Processes, IReadOnlyList<DeadlockResource> Resources, string Graph);

internal sealed record WaitDelta(string WaitType, long WaitingTasks, double WaitMilliseconds);

// What SQL Server saw during the run (--Diagnose): the costliest statements by total duration from Query Store, every deadlock
// the system_health session recorded since the load started, the server's waits over the run and the most connections each API
// process held. The container serves only the run's database, so the server's waits are the run's.
internal sealed partial record DatabaseDiagnosis(
    IReadOnlyList<QueryCost> Queries,
    IReadOnlyList<Deadlock> Deadlocks,
    string DeadlockSource,
    IReadOnlyList<WaitDelta> Waits,
    IReadOnlyList<ConnectionPeak> Connections)
{
    private const int TopQueries = 15;
    private const int TopWaits = 12;
    private const int StatementLength = 400;
    private static readonly XNamespace Showplan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    // Query Store captures every statement (not only the frequent or costly ones) in one-minute intervals, from an empty store.
    internal static async Task<IReadOnlyDictionary<string, WaitDelta>> StartAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await ExecuteAsync(connection, "ALTER DATABASE CURRENT SET QUERY_STORE = ON", cancellationToken);
        await ExecuteAsync(connection,
            "ALTER DATABASE CURRENT SET QUERY_STORE (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL, INTERVAL_LENGTH_MINUTES = 1, " +
            "DATA_FLUSH_INTERVAL_SECONDS = 60, MAX_STORAGE_SIZE_MB = 1024, WAIT_STATS_CAPTURE_MODE = ON)",
            cancellationToken);
        await ExecuteAsync(connection, "ALTER DATABASE CURRENT SET QUERY_STORE CLEAR", cancellationToken);
        return await WaitsAsync(connection, cancellationToken);
    }

    internal static async Task<DatabaseDiagnosis> ReadAsync(
        string connectionString,
        DateTimeOffset sinceUtc,
        IReadOnlyDictionary<string, WaitDelta> waitsBefore,
        IReadOnlyList<ConnectionPeak> connections,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await ExecuteAsync(connection, "EXEC sys.sp_query_store_flush_db", cancellationToken);
        var waits = await WaitsAsync(connection, cancellationToken);
        var (deadlocks, source) = await DeadlocksAsync(connection, sinceUtc, cancellationToken);
        return new(
            await QueriesAsync(connection, cancellationToken),
            deadlocks,
            source,
            waits.Values
                .Select(wait => waitsBefore.TryGetValue(wait.WaitType, out var before)
                    ? wait with { WaitingTasks = wait.WaitingTasks - before.WaitingTasks, WaitMilliseconds = wait.WaitMilliseconds - before.WaitMilliseconds }
                    : wait)
                .Where(wait => wait.WaitMilliseconds > 0)
                .OrderByDescending(wait => wait.WaitMilliseconds)
                .Take(TopWaits)
                .ToArray(),
            connections);
    }

    private static async Task<IReadOnlyList<QueryCost>> QueriesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var waits = new Dictionary<long, Dictionary<string, double>>();
        await using (var command = new SqlCommand(WaitsByQuery, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var query = reader.GetInt64(0);
                if (!waits.TryGetValue(query, out var byCategory))
                {
                    byCategory = [];
                    waits[query] = byCategory;
                }

                byCategory[reader.GetString(1)] = reader.GetInt64(2);
            }
        }

        var queries = new List<QueryCost>();
        await using (var command = new SqlCommand(CostliestQueries, connection))
        {
            command.Parameters.AddWithValue("@top", TopQueries);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                var executions = reader.GetInt64(1);
                var durationMicroseconds = reader.GetDouble(3);
                queries.Add(new QueryCost(
                    id,
                    executions,
                    reader.GetInt64(2),
                    durationMicroseconds / 1000,
                    executions == 0 ? 0 : durationMicroseconds / executions / 1000,
                    reader.GetInt64(4) / 1000.0,
                    reader.GetDouble(5) / 1000,
                    executions == 0 ? 0 : reader.GetDouble(6) / executions,
                    (waits.GetValueOrDefault(id) ?? [])
                        .OrderByDescending(wait => wait.Value)
                        .ToDictionary(wait => wait.Key, wait => wait.Value, StringComparer.Ordinal),
                    Spaces().Replace(reader.GetString(7), " ").Trim(),
                    reader.IsDBNull(8) ? "(no plan)" : PlanSummary(reader.GetString(8))));
            }
        }

        return queries;
    }

    // The operators of the plan in tree order, with the table and index each reads; a lookup is a clustered seek per row found.
    internal static string PlanSummary(string plan)
    {
        try
        {
            var operators = XDocument.Parse(plan)
                .Descendants(Showplan + "RelOp")
                .Select(Operator);
            return string.Join(" > ", operators);
        }
        catch (System.Xml.XmlException)
        {
            return "(unreadable plan)";
        }
    }

    private static string Operator(XElement relOp)
    {
        var physical = relOp.Attribute("PhysicalOp")?.Value ?? "?";
        var scan = relOp.Element(Showplan + "IndexScan");
        if (scan?.Element(Showplan + "Object") is not { } target)
        {
            return physical;
        }

        var lookup = scan.Attribute("Lookup")?.Value is "1" or "true" ? " (lookup)" : "";
        return $"{physical}{lookup} {target.Attribute("Table")?.Value}.{target.Attribute("Index")?.Value}";
    }

    // The file target keeps more history; the ring buffer is the fallback when the files cannot be read.
    private static async Task<(IReadOnlyList<Deadlock> Deadlocks, string Source)> DeadlocksAsync(
        SqlConnection connection,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var events = await ReadXmlAsync(connection, DeadlocksFromFiles, cancellationToken);
            return (Graphs(events, sinceUtc), "system_health event files");
        }
        catch (SqlException)
        {
            var buffer = await ReadXmlAsync(connection, RingBuffer, cancellationToken);
            var events = buffer
                .SelectMany(target => target.Descendants("event"))
                .Where(item => item.Attribute("name")?.Value == "xml_deadlock_report");
            return (Graphs(events, sinceUtc), "system_health ring buffer");
        }
    }

    private static IReadOnlyList<Deadlock> Graphs(IEnumerable<XElement> events, DateTimeOffset sinceUtc) => events
        .Select(item => new
        {
            AtUtc = DateTimeOffset.Parse(item.Attribute("timestamp")?.Value ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            Graph = item.Descendants("deadlock").FirstOrDefault()
        })
        .Where(item => item.AtUtc >= sinceUtc && item.Graph is not null)
        .OrderBy(item => item.AtUtc)
        .Select(item => Graph(item.AtUtc, item.Graph!))
        .ToArray();

    private static Deadlock Graph(DateTimeOffset atUtc, XElement graph)
    {
        var victims = graph.Descendants("victimProcess")
            .Select(victim => victim.Attribute("id")?.Value)
            .ToHashSet(StringComparer.Ordinal);
        var processes = graph.Descendants("process")
            .Where(process => process.Parent?.Name.LocalName == "process-list")
            .Select(process => new DeadlockProcess(
                process.Attribute("id")?.Value ?? "",
                victims.Contains(process.Attribute("id")?.Value),
                process.Attribute("lockMode")?.Value ?? "",
                process.Attribute("waitresource")?.Value ?? "",
                Statement(process)))
            .ToArray();
        var resources = graph.Element("resource-list")?.Elements()
            .Select(resource => new DeadlockResource(
                resource.Name.LocalName,
                resource.Attribute("objectname")?.Value ?? "",
                resource.Attribute("indexname")?.Value ?? "",
                Locks(resource, "owner"),
                Locks(resource, "waiter")))
            .ToArray() ?? [];
        return new(atUtc, processes, resources, graph.ToString(SaveOptions.DisableFormatting));
    }

    private static string Locks(XElement resource, string role) => string.Join(", ", resource.Descendants(role)
        .Select(owner => $"{owner.Attribute("id")?.Value} {owner.Attribute("mode")?.Value}{(owner.Attribute("requestType")?.Value is { } request ? " " + request : "")}"));

    // The statement the process ran: the innermost frame's text, else (a parameterised batch's frame says "unknown") the input
    // buffer.
    private static string Statement(XElement process)
    {
        var text = process.Descendants("frame")
            .Select(frame => frame.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? process.Element("inputbuf")?.Value
            ?? "";
        var frame = Spaces().Replace(text, " ").Trim();
        var statement = frame == "unknown" ? Spaces().Replace(process.Element("inputbuf")?.Value ?? "", " ").Trim() : frame;
        return statement[..Math.Min(statement.Length, StatementLength)];
    }

    private static async Task<Dictionary<string, WaitDelta>> WaitsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var waits = new Dictionary<string, WaitDelta>(StringComparer.Ordinal);
        await using var command = new SqlCommand("SELECT wait_type, waiting_tasks_count, wait_time_ms FROM sys.dm_os_wait_stats WHERE wait_time_ms > 0", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            waits[reader.GetString(0)] = new WaitDelta(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2));
        }

        return waits;
    }

    private static async Task<IReadOnlyList<XElement>> ReadXmlAsync(SqlConnection connection, string query, CancellationToken cancellationToken)
    {
        var documents = new List<XElement>();
        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(XElement.Parse(reader.GetString(0)));
        }

        return documents;
    }

    private static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string statement, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(statement, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Durations and CPU are in microseconds; the plan is the one the statement ran with most often.
    private const string CostliestQueries = """
        WITH PlanStats AS (
            SELECT p.query_id, p.plan_id,
                SUM(rs.count_executions) AS executions,
                SUM(CASE WHEN rs.execution_type <> 0 THEN rs.count_executions ELSE 0 END) AS failed,
                SUM(rs.avg_duration * rs.count_executions) AS duration,
                MAX(rs.max_duration) AS max_duration,
                SUM(rs.avg_cpu_time * rs.count_executions) AS cpu,
                SUM(rs.avg_logical_io_reads * rs.count_executions) AS reads
            FROM sys.query_store_runtime_stats AS rs
            JOIN sys.query_store_plan AS p ON p.plan_id = rs.plan_id
            GROUP BY p.query_id, p.plan_id)
        SELECT TOP (@top) s.query_id, SUM(s.executions), SUM(s.failed), SUM(s.duration), MAX(s.max_duration), SUM(s.cpu), SUM(s.reads),
            MAX(t.query_sql_text),
            (SELECT TOP (1) plans.query_plan FROM PlanStats AS ranked JOIN sys.query_store_plan AS plans ON plans.plan_id = ranked.plan_id
                WHERE ranked.query_id = s.query_id ORDER BY ranked.executions DESC)
        FROM PlanStats AS s
        JOIN sys.query_store_query AS q ON q.query_id = s.query_id
        JOIN sys.query_store_query_text AS t ON t.query_text_id = q.query_text_id
        GROUP BY s.query_id
        ORDER BY SUM(s.duration) DESC
        """;

    private const string WaitsByQuery = """
        SELECT p.query_id, w.wait_category_desc, SUM(w.total_query_wait_time_ms)
        FROM sys.query_store_wait_stats AS w
        JOIN sys.query_store_plan AS p ON p.plan_id = w.plan_id
        GROUP BY p.query_id, w.wait_category_desc
        """;

    private const string DeadlocksFromFiles = """
        SELECT CAST(event_data AS nvarchar(max))
        FROM sys.fn_xe_file_target_read_file('system_health*.xel', NULL, NULL, NULL)
        WHERE object_name = 'xml_deadlock_report'
        """;

    private const string RingBuffer = """
        SELECT CAST(t.target_data AS nvarchar(max))
        FROM sys.dm_xe_session_targets AS t
        JOIN sys.dm_xe_sessions AS s ON s.address = t.event_session_address
        WHERE s.name = 'system_health' AND t.target_name = 'ring_buffer'
        """;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
