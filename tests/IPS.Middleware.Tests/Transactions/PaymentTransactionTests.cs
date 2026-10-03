using IPS.Middleware.Domain.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Transactions;

public sealed class PaymentTransactionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creation_normalizes_references_and_records_received_in_utc()
    {
        var id = Guid.NewGuid();
        var observedAt = Start.ToOffset(TimeSpan.FromHours(4));
        var transaction = new PaymentTransaction(id, " pacs.008 ", TransactionDirection.Outgoing,
            observedAt, " CBS-1 ", "  ");

        Assert.Equal(id, transaction.Id);
        Assert.Equal("pacs.008", transaction.MessageType);
        Assert.Equal(TransactionDirection.Outgoing, transaction.Direction);
        Assert.Equal("CBS-1", transaction.ClientReference);
        Assert.Null(transaction.CoreReference);
        Assert.Equal(Start, transaction.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, transaction.CreatedAtUtc.Offset);
        var received = Assert.Single(transaction.History);
        Assert.Same(received, transaction.Current);
        Assert.Equal(TransactionStatus.Received, received.Status);
        Assert.Equal(StatusSource.Gateway, received.Source);
        Assert.Equal(1, received.Sequence);
        Assert.Equal(Start, received.AtUtc);
        Assert.Equal(TimeSpan.Zero, received.AtUtc.Offset);
        Assert.Null(received.Step);
        Assert.Null(received.ReasonCode);
        Assert.Null(received.IpsInternalCode);
        Assert.Null(received.Description);
    }

    [Fact]
    public void Incoming_creation_allows_missing_references_and_unrecognized_message_names()
    {
        var transaction = new PaymentTransaction(Guid.NewGuid(), "custom.message", TransactionDirection.Incoming, Start);

        Assert.Equal(TransactionDirection.Incoming, transaction.Direction);
        Assert.Equal("custom.message", transaction.MessageType);
        Assert.Null(transaction.ClientReference);
        Assert.Null(transaction.CoreReference);
        Assert.Equal(TransactionStatus.Received, transaction.Current.Status);
        Assert.False(transaction.IsFinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Creation_requires_a_message_type(string? messageType)
    {
        Assert.Throws<ArgumentException>(() =>
            new PaymentTransaction(Guid.NewGuid(), messageType!, TransactionDirection.Outgoing, Start));
    }

    [Fact]
    public void Creation_rejects_empty_identity_and_undefined_direction()
    {
        Assert.Throws<ArgumentException>(() =>
            new PaymentTransaction(Guid.Empty, "pacs.008", TransactionDirection.Outgoing, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PaymentTransaction(Guid.NewGuid(), "pacs.008", (TransactionDirection)99, Start));
    }

    [Fact]
    public void Rejection_preserves_status_changes_and_processing_steps()
    {
        var transaction = Create();
        transaction.RecordStep(ProcessingStep.XmlGenerated, Start.AddMilliseconds(5));
        transaction.ChangeStatus(TransactionStatus.Sending, StatusSource.Gateway, Start.AddMilliseconds(10));
        var rejected = transaction.ChangeStatus(TransactionStatus.Rejected, StatusSource.Ips,
            Start.AddSeconds(1), " ac01 ", 1009, " The creditor IBAN code is invalid. ");

        Assert.Same(rejected, transaction.Current);
        Assert.Equal("AC01", transaction.Current.ReasonCode);
        Assert.Equal(1009, transaction.Current.IpsInternalCode);
        Assert.Equal("The creditor IBAN code is invalid.", transaction.Current.Description);
        Assert.True(transaction.IsFinal);
        Assert.Equal(new[] { 1, 2, 3, 4 }, transaction.History.Select(entry => entry.Sequence));
        Assert.Collection(transaction.History,
            entry => Assert.Equal((TransactionStatus.Received, (ProcessingStep?)null), (entry.Status, entry.Step)),
            entry => Assert.Equal((TransactionStatus.Received, (ProcessingStep?)ProcessingStep.XmlGenerated), (entry.Status, entry.Step)),
            entry => Assert.Equal((TransactionStatus.Sending, (ProcessingStep?)null), (entry.Status, entry.Step)),
            entry => Assert.Same(rejected, entry));
        Assert.Equal(StatusSource.Ips, rejected.Source);
    }

    [Fact]
    public void Processing_step_preserves_current_outcome_and_earlier_observations()
    {
        var transaction = Create();
        var received = transaction.Current;
        var rejected = transaction.ChangeStatus(TransactionStatus.Rejected, StatusSource.Core, Start,
            "AC04", 1009, "Account closed");
        var step = transaction.RecordStep(ProcessingStep.RepliedToIps, Start.AddSeconds(1));

        Assert.Same(rejected, transaction.Current);
        Assert.Equal(Start, transaction.Current.AtUtc);
        Assert.Equal("AC04", transaction.Current.ReasonCode);
        Assert.Equal(1009, transaction.Current.IpsInternalCode);
        Assert.Equal("Account closed", transaction.Current.Description);
        Assert.Equal(TransactionStatus.Received, received.Status);
        Assert.Null(received.ReasonCode);
        Assert.Equal(TransactionStatus.Rejected, step.Status);
        Assert.Equal(ProcessingStep.RepliedToIps, step.Step);
        Assert.Equal(StatusSource.Gateway, step.Source);
        Assert.Null(step.ReasonCode);
        Assert.Null(step.IpsInternalCode);
        Assert.Null(step.Description);
        Assert.Equal(Start.AddSeconds(1), step.AtUtc);
    }

    [Fact]
    public void New_status_clears_previous_explanation_when_no_details_are_supplied()
    {
        var transaction = Create();
        var uncertain = transaction.ChangeStatus(TransactionStatus.Uncertain, StatusSource.Gateway, Start,
            "MS03", 1000, "Reply lost");
        transaction.ChangeStatus(TransactionStatus.Investigating, StatusSource.Recovery, Start.AddSeconds(1));

        Assert.Null(transaction.Current.ReasonCode);
        Assert.Null(transaction.Current.IpsInternalCode);
        Assert.Null(transaction.Current.Description);
        Assert.Equal("MS03", uncertain.ReasonCode);
        Assert.Equal(1000, uncertain.IpsInternalCode);
        Assert.Equal("Reply lost", uncertain.Description);
    }

    [Fact]
    public void Repeated_states_and_equal_or_earlier_times_remain_separate_observations()
    {
        var transaction = Create();
        transaction.ChangeStatus(TransactionStatus.Received, StatusSource.Gateway, Start);
        transaction.ChangeStatus(TransactionStatus.Sending, StatusSource.Gateway, Start.AddSeconds(-1));

        Assert.Equal(new[] { 1, 2, 3 }, transaction.History.Select(entry => entry.Sequence));
        Assert.Equal(new[] { Start, Start, Start.AddSeconds(-1) }, transaction.History.Select(entry => entry.AtUtc));
        Assert.Equal(TransactionStatus.Sending, transaction.Current.Status);
        Assert.Equal(Start.AddSeconds(-1), transaction.Current.AtUtc);
    }

    [Fact]
    public void History_cannot_be_removed_by_casting_and_retained_view_sees_new_observations()
    {
        var transaction = Create();
        var history = transaction.History;
        var collection = Assert.IsAssignableFrom<IList<TransactionHistoryEntry>>(history);

        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        transaction.ChangeStatus(TransactionStatus.Sending, StatusSource.Gateway, Start);
        Assert.Equal(2, history.Count);
        Assert.Same(transaction.Current, history[1]);
    }

    [Theory]
    [InlineData(TransactionStatus.Received, false)]
    [InlineData(TransactionStatus.Sending, false)]
    [InlineData(TransactionStatus.Accepted, true)]
    [InlineData(TransactionStatus.Rejected, true)]
    [InlineData(TransactionStatus.NotSent, true)]
    [InlineData(TransactionStatus.Uncertain, false)]
    [InlineData(TransactionStatus.Investigating, false)]
    [InlineData(TransactionStatus.Resending, false)]
    [InlineData(TransactionStatus.ManualReview, false)]
    [InlineData(TransactionStatus.ManuallyResolved, true)]
    [InlineData(TransactionStatus.CoreUnknown, false)]
    public void Final_classification_matches_the_reference(TransactionStatus status, bool expected)
    {
        var transaction = Create();
        transaction.ChangeStatus(status, StatusSource.Gateway, Start);

        Assert.Equal(expected, transaction.IsFinal);
    }

    [Fact]
    public void Final_classification_does_not_invent_a_global_transition_restriction()
    {
        var transaction = Create();
        var rejected = transaction.ChangeStatus(TransactionStatus.Rejected, StatusSource.Core, Start, "AC04");
        transaction.ChangeStatus(TransactionStatus.ManuallyResolved, StatusSource.Operator, Start.AddHours(1),
            description: "Operator reconciled the outcome");

        Assert.Equal(TransactionStatus.ManuallyResolved, transaction.Current.Status);
        Assert.True(transaction.IsFinal);
        Assert.Equal(TransactionStatus.Rejected, rejected.Status);
        Assert.Equal("AC04", rejected.ReasonCode);
        Assert.Equal(3, transaction.History.Count);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" ac01 ", "AC01")]
    public void Status_details_normalize_reasons_and_blank_descriptions(string? input, string? expected)
    {
        var transaction = Create();
        var changed = transaction.ChangeStatus(TransactionStatus.Rejected, StatusSource.Ips,
            Start.ToOffset(TimeSpan.FromHours(4)), input, description: "  ");

        Assert.Equal(expected, changed.ReasonCode);
        Assert.Null(changed.Description);
        Assert.Equal(TimeSpan.Zero, changed.AtUtc.Offset);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2000)]
    [InlineData(2001)]
    public void Description_is_trimmed_and_bounded_consistently(int length)
    {
        var transaction = Create();
        var changed = transaction.ChangeStatus(TransactionStatus.Rejected, StatusSource.Ips, Start,
            description: "  " + new string('x', length) + "  ");

        Assert.Equal(new string('x', Math.Min(length, 2000)), changed.Description);
        Assert.Same(changed, transaction.Current);
        Assert.Same(changed, transaction.History[^1]);
    }

    [Fact]
    public void Invalid_internal_values_do_not_partially_change_history()
    {
        var transaction = Create();
        var received = transaction.Current;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transaction.ChangeStatus((TransactionStatus)99, StatusSource.Gateway, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transaction.ChangeStatus(TransactionStatus.Sending, (StatusSource)99, Start));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transaction.RecordStep((ProcessingStep)99, Start));
        Assert.Single(transaction.History);
        Assert.Same(received, transaction.Current);
    }

    private static PaymentTransaction Create() =>
        new(Guid.NewGuid(), "pacs.008", TransactionDirection.Outgoing, Start, clientReference: "CBS-1");
}
