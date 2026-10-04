using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IPS.Middleware.IntegrationTests.Transactions;

internal sealed class SqlTestDatabase : IAsyncDisposable
{
    private const string Prefix = "IPS_Middleware_Tests_";
    private readonly string _databaseName = Prefix + Guid.NewGuid().ToString("N");
    private readonly string _connectionString;

    private SqlTestDatabase()
    {
        _connectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = _databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 30
        }.ConnectionString;
    }

    public TransactionDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<TransactionDbContext>()
        .UseSqlServer(_connectionString).AddInterceptors(interceptors).Options);
    public PaymentSession Session(params IInterceptor[] interceptors) => new(Context(interceptors));

    public static async Task<SqlTestDatabase> CreateAsync()
    {
        var database = new SqlTestDatabase();
        try
        {
            await using var db = database.Context();
            await db.Database.MigrateAsync();
            return database;
        }
        catch { await database.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        var connection = new SqlConnectionStringBuilder(_connectionString);
        if (connection.DataSource != @"(localdb)\MSSQLLocalDB" ||
            connection.InitialCatalog != _databaseName || !_databaseName.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to delete a database outside this test fixture.");
        await using var db = Context();
        await db.Database.EnsureDeletedAsync();
        using var poolConnection = new SqlConnection(_connectionString);
        SqlConnection.ClearPool(poolConnection);
    }
}

internal sealed class PaymentSession : IAsyncDisposable
{
    public PaymentSession(TransactionDbContext context)
    {
        Context = context;
        Payments = new(context);
        Work = new(context);
        Submissions = new(context);
        Unit = new(context);
    }
    public TransactionDbContext Context { get; }
    public OutgoingPaymentRepository Payments { get; }
    public TransactionWorkRepository Work { get; }
    public PaymentSubmissionRepository Submissions { get; }
    public UnitOfWork Unit { get; }
    public OutgoingTransactionIntake Intake(DateTimeOffset now) => new(Payments, Unit, new FixedClock(now));
    public OutgoingTransactionWork Processing(DateTimeOffset now) => new(Payments, Work, Submissions, Unit, new FixedClock(now));
    public ValueTask DisposeAsync() => Context.DisposeAsync();
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
