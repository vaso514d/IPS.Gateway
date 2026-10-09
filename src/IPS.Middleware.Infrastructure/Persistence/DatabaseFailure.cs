using Microsoft.Data.SqlClient;

namespace IPS.Middleware.Infrastructure.Persistence;

public static class DatabaseFailure
{
    // SQL Server errors a repeat can succeed after: a command timeout, a deadlock victim, a lock request timeout, and broken,
    // refused or throttled connections. Anything else (a missing object, a truncation, a constraint) is a defect that repeating
    // the request cannot fix, so it stays an error.
    private static readonly HashSet<int> TransientNumbers =
        [-2, 64, 233, 1205, 1222, 4060, 4221, 10053, 10054, 10060, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920];

    // SqlClient reports an exhausted connection pool as this InvalidOperationException, with no SqlException inside.
    private const string PoolTimeout = "obtaining a connection from the pool";

    // EF Core reports a failure its SQL Server detector classifies as transient inside an InvalidOperationException, because no
    // retrying execution strategy is configured, so the inner exceptions are searched too.
    public static bool IsTransient(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            var transient = current switch
            {
                SqlException sql => sql.Errors.Cast<SqlError>().Any(failure => IsTransient(failure.Number, failure.Class)),
                InvalidOperationException pool => pool.Message.Contains(PoolTimeout, StringComparison.Ordinal),
                _ => false
            };
            if (transient)
            {
                return true;
            }
        }

        return false;
    }

    // Severity 20 and above means SQL Server ended the connection ("A severe error occurred").
    internal static bool IsTransient(int number, byte severity) => TransientNumbers.Contains(number) || severity >= 20;
}
