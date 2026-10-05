using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Persistence;

public sealed class TypedPaymentMetadataTests
{
    [Fact]
    public void Technical_state_is_typed_and_required_on_the_same_versioned_payment_row()
    {
        using var context = new DesignTimeTransactionDbContextFactory().CreateDbContext([]);
        CheckPair<OutgoingPayment, OutgoingPaymentMetadata>(context, "Transactions", "RequestJson", "Direction");
        CheckPair<IncomingPayment, IncomingPaymentMetadata>(context, "IncomingPayments", "RequestJson", "ContextJson", "CheckpointVersion");
    }

    [Fact]
    public void Repository_authorization_captures_values_including_mutable_rowversion_bytes()
    {
        using var context = new DesignTimeTransactionDbContextFactory().CreateDbContext([]);
        var row = new OutgoingStatusDeliveryRow { PaymentId = Guid.NewGuid(), Sequence = 1, PayloadJson = "{}", RowVersion = [1, 2] };
        var entry = context.Attach(row);
        var changes = new StagedChanges<OutgoingStatusDeliveryRow>();
        changes.Authorize(entry);
        Assert.True(changes.Matches(entry));
        row.RowVersion[0] = 3;
        Assert.False(changes.Matches(entry));
        row.RowVersion = [1, 2];
        Assert.True(changes.Matches(entry));
        row.PayloadJson = "changed";
        Assert.False(changes.Matches(entry));
    }

    private static void CheckPair<TPayment, TMetadata>(TransactionDbContext context, string table, params string[] required)
    {
        var payment = context.Model.FindEntityType(typeof(TPayment))!;
        var metadata = context.Model.FindEntityType(typeof(TMetadata))!;
        Assert.Equal(table, payment.GetTableName());
        Assert.Equal(table, metadata.GetTableName());
        Assert.All(required, name =>
        {
            Assert.Null(payment.FindProperty(name));
            var property = metadata.FindProperty(name)!;
            Assert.False(property.IsShadowProperty());
            Assert.False(property.IsColumnNullable());
        });
        Assert.True(payment.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.True(metadata.FindProperty("RowVersion")!.IsConcurrencyToken);
    }
}
