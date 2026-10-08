using System.Diagnostics.Metrics;
using IPS.Middleware.Application.Diagnostics;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.Pain002;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Diagnostics;

public sealed class ValidationRejectionMetricTests
{
    [Fact]
    public async Task A_rejected_outgoing_request_is_counted_by_its_request_type()
    {
        var counted = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "ips.validation.rejections")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => counted.Add((string)tags[0].Value!));
        listener.Start();
        var submission = new OutgoingSubmission(new Rejecting(), new OutgoingExecutionOptions(enabled: true), TimeProvider.System);

        var result = await submission.SubmitAsync(new Pain002Request(), "{}", default);

        Assert.Null(result.Status);
        // The meter is process-wide, so count only this test's request type.
        Assert.Equal(1, counted.Count(operation => operation == "Pain002Request"));
    }

    private sealed class Rejecting : IOutgoingExecution
    {
        public Task<OutgoingAcceptance> AcceptAsync(IOutgoingPaymentRequest request, string json, CancellationToken token) =>
            Task.FromResult(new OutgoingAcceptance(null, [new IntakeValidationError("reasonCode", "required")]));

        public bool TryStart(Guid paymentId) => false;

        public Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken token) => Task.FromResult<OutgoingStatus?>(null);
    }
}
