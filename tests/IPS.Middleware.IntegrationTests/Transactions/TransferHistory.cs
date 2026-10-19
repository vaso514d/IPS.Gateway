using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.History;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.IntegrationTests.Transactions;

internal sealed record TransferHistoryRow(
    TransactionDirection Direction,
    string MessageType,
    TransactionStatus Status,
    StatusSource Source,
    ProcessingStep? Step,
    string? ReasonCode,
    int? IpsInternalCode);

internal static class TransferHistory
{
    internal static async Task<IReadOnlyList<TransferHistoryRow>> ReadAsync(SqlTestDatabase database, Guid paymentId)
    {
        await using var db = database.Context();
        return await db.Set<TransferStatusRow>()
            .Where(row => row.PaymentTransactionId == paymentId)
            .OrderBy(row => row.Id)
            .Select(row => new TransferHistoryRow(row.Direction, row.MessageType, row.Status, row.Source, row.Step, row.ReasonCode, row.IpsInternalCode))
            .ToListAsync();
    }
}
