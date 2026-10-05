using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs008;

// Processes an accepted pacs.008, resuming from its committed checkpoints.
public sealed class Pacs008Processing(
    IOutgoingPaymentRepository payments,
    ITransactionWorkRepository work,
    IPaymentPreparationRepository preparation,
    IPaymentSubmissionRepository submissions,
    IUnitOfWork unitOfWork,
    IPacs008MessagePreparation protocol,
    IIpsTransport transport,
    IIpsReplyInterpreter replies,
    Pacs008Options options,
    TimeProvider timeProvider)
{
    private const string SubmittedWithoutResponse = "Submission may have reached IPS but no response was stored; investigate before any resend.";
    private const string AcceptedDataUnavailable = "Accepted payment data is unavailable; nothing was sent.";
    private const string ResponseNotCorrelatable = "The stored response cannot be correlated without accepted payment data.";
    private const string DevelopmentDispositionRefused =
        "The frozen development-unsigned message cannot be replaced; current signing policy did not authorize its disposition.";

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    // Returns the last outcome this run committed, or null when the payment does not exist.
    public async Task<PaymentOutcome?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await payments.FindAsync(paymentId, cancellationToken);
        if (payment is null)
        {
            return null;
        }

        if (!IsProcessable(payment))
        {
            return payment.Current;
        }

        var now = Now;
        var claimed = ClaimedPayment.TryStage(payment, work, unitOfWork, now, options.Ownership);
        if (claimed is null)
        {
            return payment.Current;
        }

        if (payment.CurrentStatus == TransactionStatus.Received)
        {
            payment.BeginSending(now);
        }

        try
        {
            await claimed.CommitAsync(cancellationToken);
            await ContinueAsync(claimed, cancellationToken);
        }
        catch (PersistenceConcurrencyException)
        {
            // Another owner or recovery won. This scope is discarded; report what this run committed.
        }

        return claimed.Committed;
    }

    private async Task ContinueAsync(ClaimedPayment claimed, CancellationToken cancellationToken)
    {
        var payment = claimed.Payment;
        var message = await preparation.ReadAsync(payment.Id, cancellationToken)
            ?? throw new InvalidOperationException("A pacs.008 requires stored protocol identifiers.");
        var submission = await submissions.ReadAsync(payment.Id, cancellationToken);

        if (submission?.Response is { } storedResponse)
        {
            await InterpretAsync(claimed, message, storedResponse, cancellationToken);
            return;
        }

        if (submission?.Marker is not null)
        {
            payment.MarkOutcomeUnknown(StatusSource.Gateway, Now, new PaymentDetails(description: SubmittedWithoutResponse));
            await claimed.ReleaseAsync(Now, null, cancellationToken);
            return;
        }

        if (message.Accepted is not { } accepted)
        {
            payment.RecordNotSent(Now, new PaymentDetails(description: AcceptedDataUnavailable));
            await claimed.ReleaseAsync(Now, null, cancellationToken);
            return;
        }

        if (await ExpiredAsync(claimed, accepted, cancellationToken))
        {
            return;
        }

        await SubmitAsync(claimed, message, accepted, cancellationToken);
    }

    private async Task SubmitAsync(ClaimedPayment claimed, PreparedPaymentMessage message, AcceptedPacs008 accepted, CancellationToken cancellationToken)
    {
        var payment = claimed.Payment;
        var signing = await PrepareAsync(claimed, message, accepted, cancellationToken);
        if (signing is SigningDeferred deferred)
        {
            payment.RecordProcessingFailure(ProcessingStep.Signed, Now, deferred.Reason);
            await claimed.ReleaseAsync(Now, Now + options.PreparationRetryDelay, cancellationToken);
            return;
        }

        if (await ExpiredAsync(claimed, accepted, cancellationToken))
        {
            return;
        }

        var signed = (SignedMessage)signing;
        await ReserveSubmissionAsync(claimed, signed, cancellationToken);

        // IPS may act on the message now; its response and the outcome are stored even if the caller stops waiting.
        IpsSubmissionResponse response;
        try
        {
            response = await transport.SendAsync(signed.Xml, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            using var evidence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
            var details = new PaymentDetails(description: $"{SubmittedWithoutResponse} {exception.GetType().Name}: {exception.Message}");
            payment.MarkOutcomeUnknown(StatusSource.Gateway, Now, details);
            await claimed.ReleaseAsync(Now, null, evidence.Token);
            return;
        }

        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        submissions.StageResponse(payment, claimed.Claim, response, Now);
        payment.RecordStep(ProcessingStep.IpsResponded, Now);
        await claimed.CommitAsync(persistence.Token);
        await InterpretAsync(claimed, message, response, persistence.Token);
    }

    // Each artifact is committed before the next step; stored artifacts are reused unchanged.
    private async Task<SigningResult> PrepareAsync(
        ClaimedPayment claimed,
        PreparedPaymentMessage message,
        AcceptedPacs008 accepted,
        CancellationToken cancellationToken)
    {
        if (message.SignedXml is { } storedSignature)
        {
            return new SignedMessage(storedSignature, SubmissionMessageKind.Signed);
        }

        var unsignedXml = message.UnsignedXml;
        if (unsignedXml is null)
        {
            unsignedXml = protocol.BuildUnsignedXml(accepted, message.MessageId, message.TransactionId);
            preparation.StageUnsignedXml(claimed.Payment, claimed.Claim, unsignedXml, Now);
            claimed.Payment.RecordStep(ProcessingStep.XmlGenerated, Now);
            await claimed.CommitAsync(cancellationToken);
        }

        // The current host policy decides each time whether development may submit unsigned XML.
        var signing = await protocol.SignAsync(unsignedXml, cancellationToken);
        var frozenAsDevelopment = message.ReadyDisposition == SubmissionMessageKind.DevelopmentUnsigned;
        if (frozenAsDevelopment && signing is not SignedMessage { Kind: SubmissionMessageKind.DevelopmentUnsigned })
        {
            return new SigningDeferred(DevelopmentDispositionRefused);
        }

        if (signing is not SignedMessage { Kind: SubmissionMessageKind.Signed } signature)
        {
            return signing;
        }

        preparation.StageSignedXml(claimed.Payment, claimed.Claim, signature.Xml, Now);
        claimed.Payment.RecordStep(ProcessingStep.Signed, Now);
        await claimed.CommitAsync(cancellationToken);
        return signature;
    }

    // The marker commits before remote I/O: from here on a lost reply is uncertain, never resent.
    private async Task ReserveSubmissionAsync(ClaimedPayment claimed, SignedMessage signed, CancellationToken cancellationToken)
    {
        if (signed.Kind == SubmissionMessageKind.DevelopmentUnsigned)
        {
            preparation.StageDevelopmentUnsigned(claimed.Payment, claimed.Claim, Now);
            await claimed.CommitAsync(cancellationToken);
        }

        submissions.StageSubmission(claimed.Payment, claimed.Claim, signed.Kind, Now);
        await claimed.CommitAsync(cancellationToken);
    }

    private Task InterpretAsync(
        ClaimedPayment claimed,
        PreparedPaymentMessage message,
        IpsSubmissionResponse response,
        CancellationToken cancellationToken)
    {
        var payment = claimed.Payment;
        if (message.Accepted is null)
        {
            payment.MarkOutcomeUnknown(StatusSource.Gateway, Now, new PaymentDetails(description: ResponseNotCorrelatable));
            return claimed.ReleaseAsync(Now, null, cancellationToken);
        }

        var correlation = new IpsReplyCorrelation(message.MessageId, message.TransactionId, message.Accepted.Payment.EndToEndId);
        var reply = replies.Interpret(response, correlation);
        submissions.StageInterpretation(payment, claimed.Claim, reply, Now);
        RecordReply(payment, reply, Now);
        return claimed.ReleaseAsync(Now, null, cancellationToken);
    }

    // The deadline only prevents a first send; once a marker exists, expiry cannot establish NotSent.
    private async Task<bool> ExpiredAsync(ClaimedPayment claimed, AcceptedPacs008 accepted, CancellationToken cancellationToken)
    {
        if (Now <= accepted.SubmissionDeadlineUtc)
        {
            return false;
        }

        claimed.Payment.RecordNotSent(Now, new PaymentDetails("TM01", 1015, "Not sent: the submission deadline passed before submission."));
        await claimed.ReleaseAsync(Now, null, cancellationToken);
        return true;
    }

    private static bool IsProcessable(OutgoingPayment payment) =>
        payment.MessageType == PaymentMessageTypes.Pacs008
        && payment.CurrentStatus is TransactionStatus.Received or TransactionStatus.Sending;

    private static void RecordReply(OutgoingPayment payment, IpsReply reply, DateTimeOffset observedAt)
    {
        switch (reply.Status)
        {
            case IpsReplyStatus.Accepted:
                payment.RecordAcceptance(StatusSource.Ips, observedAt, reply.Details);
                break;
            case IpsReplyStatus.Rejected:
                payment.RecordRejection(StatusSource.Ips, observedAt, reply.Details);
                break;
            default:
                payment.MarkOutcomeUnknown(StatusSource.Ips, observedAt, reply.Details);
                break;
        }
    }
}
