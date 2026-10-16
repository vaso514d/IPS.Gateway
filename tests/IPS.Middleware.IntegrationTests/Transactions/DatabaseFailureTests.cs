using IPS.Middleware.Infrastructure.Persistence;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transactions;

// Only a failure a repeat can succeed after answers 503; a permanent one stays an error, or the core would repeat forever.
public sealed class DatabaseFailureTests
{
    [Theory]
    [InlineData(-2, 11, true)]
    [InlineData(1205, 13, true)]
    [InlineData(1222, 16, true)]
    [InlineData(10054, 20, true)]
    [InlineData(0, 20, true)]
    [InlineData(547, 16, false)]
    [InlineData(2627, 14, false)]
    [InlineData(2628, 16, false)]
    [InlineData(208, 16, false)]
    [InlineData(229, 14, false)]
    public void A_sql_error_is_transient_only_by_its_number_or_a_severity_that_ended_the_connection(int number, byte severity, bool transient) =>
        Assert.Equal(transient, DatabaseFailure.IsTransient(number, severity));

    [Fact]
    public void An_exhausted_connection_pool_is_transient_and_other_failures_are_not()
    {
        var pool = new InvalidOperationException(
            "Timeout expired.  The timeout period elapsed prior to obtaining a connection from the pool.  This may have occurred because all pooled connections were in use and max pool size was reached.");

        Assert.True(DatabaseFailure.IsTransient(pool));
        Assert.True(DatabaseFailure.IsTransient(new InvalidOperationException("wrapped", pool)));
        Assert.False(DatabaseFailure.IsTransient(new InvalidOperationException("Durable intake has no current status.")));
        Assert.False(DatabaseFailure.IsTransient(new ArgumentException("bad")));
    }
}
