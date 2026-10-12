using IPS.Middleware.Application.Payments.StatusDelivery;
using Microsoft.Data.SqlClient;

namespace IPS.Middleware.Performance;

// The service's record of one callback delivery: the outcome sequence it reports, its state, the attempts claimed for it (each
// claim may call the core once) and the reason the last attempt that did not end delivered gave.
internal sealed record DeliveryRecord(int Sequence, StatusDeliveryState State, int Attempts, string? LastFailure);

internal static class DeliveryRecords
{
    // By the payment's client reference, read after the drain.
    internal static async Task<ILookup<string, DeliveryRecord>> ReadAsync(string connectionString, CancellationToken cancellationToken)
    {
        var records = new List<(string Reference, DeliveryRecord Record)>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT t.ClientReference, d.Sequence, d.State, d.Attempts, d.LastFailure " +
            "FROM OutgoingStatusDeliveries AS d JOIN Transactions AS t ON t.Id = d.PaymentId",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new DeliveryRecord(
                reader.GetInt32(1),
                (StatusDeliveryState)reader.GetInt32(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
            records.Add((reader.GetString(0), record));
        }

        return records.ToLookup(item => item.Reference, item => item.Record, StringComparer.Ordinal);
    }
}
