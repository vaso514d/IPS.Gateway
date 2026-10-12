using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using DomainStatus = IPS.Middleware.Domain.Transactions.TransactionStatus;

namespace IPS.Middleware.Performance;

internal sealed record DueWork(string Kind, long Count);

// The work the service still has, read from its database. Due repeats, kind by kind, the query behind the ips.backlog.due gauge
// (BacklogReader), which the service publishes only in its own process; the drain waits for NotFinalPayments and PendingCallbacks.
// The service's database sets are internal, so these are SQL queries over the stored enum values.
internal sealed record Backlog(IReadOnlyList<DueWork> Due, long NotFinalPayments, long PendingCallbacks)
{
    internal bool Drained => NotFinalPayments == 0 && PendingCallbacks == 0;

    internal bool NothingDue => Due.All(work => work.Count == 0);

    internal static async Task<Backlog> ReadAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var database = new TransactionDbContext(new DbContextOptionsBuilder<TransactionDbContext>().UseSqlServer(connectionString).Options);
        var now = DateTimeOffset.UtcNow;
        var pendingReceipt = (int)InboundProcessingStatus.Pending;
        var pendingDelivery = (int)StatusDeliveryState.Pending;
        DueWork[] due =
        [
            new("outgoing_payments", await CountAsync(database, $"SELECT COUNT_BIG(*) AS [Value] FROM [Transactions] WHERE [NextActionAtUtc] <= {now}", cancellationToken)),
            new("incoming_payments", await CountAsync(database, $"SELECT COUNT_BIG(*) AS [Value] FROM [IncomingPayments] WHERE [NextActionAtUtc] <= {now}", cancellationToken)),
            new("incoming_transfers", await CountAsync(database, $"SELECT COUNT_BIG(*) AS [Value] FROM [IncomingTransfers] WHERE [NextActionAtUtc] <= {now}", cancellationToken)),
            new("inbound_receipts", await CountAsync(database, $"SELECT COUNT_BIG(*) AS [Value] FROM [InboundMessageJournal] WHERE [Status] = {pendingReceipt} AND [NextActionAtUtc] <= {now}", cancellationToken)),
            new("callbacks", await CountAsync(database, $"SELECT COUNT_BIG(*) AS [Value] FROM [OutgoingStatusDeliveries] WHERE [State] = {pendingDelivery} AND [NextAtUtc] <= {now}", cancellationToken))
        ];
        return new Backlog(
            due,
            await CountAsync(database, NotFinal(), cancellationToken),
            await CountAsync(database, $"SELECT COUNT_BIG(*) AS [Value] FROM [OutgoingStatusDeliveries] WHERE [State] = {pendingDelivery}", cancellationToken));
    }

    // The final statuses are those of OutgoingPayment.IsFinal.
    private static FormattableString NotFinal()
    {
        var accepted = (int)DomainStatus.Accepted;
        var rejected = (int)DomainStatus.Rejected;
        var notSent = (int)DomainStatus.NotSent;
        var manuallyResolved = (int)DomainStatus.ManuallyResolved;
        return $"SELECT COUNT_BIG(*) AS [Value] FROM [Transactions] WHERE [CurrentStatus] NOT IN ({accepted}, {rejected}, {notSent}, {manuallyResolved})";
    }

    private static async Task<long> CountAsync(TransactionDbContext database, FormattableString query, CancellationToken cancellationToken) =>
        await database.Database.SqlQuery<long>(query).SingleAsync(cancellationToken);
}
