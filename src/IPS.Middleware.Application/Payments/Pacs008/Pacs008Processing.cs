using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs008;

/// <summary>Processes an accepted pacs.008, resuming from its committed checkpoints.</summary>
public sealed class Pacs008Processing(
    IOutgoingPaymentRepository payments, ITransactionWorkRepository work, IPaymentPreparationRepository preparation,
    IPaymentSubmissionRepository submissions, IUnitOfWork unitOfWork, IPacs008MessagePreparation protocol,
    IIpsTransport transport, IIpsReplyInterpreter replies, Pacs008Options options, TimeProvider timeProvider)
{
    private const string SubmittedWithoutResponse =
        "Submission may have reached IPS but no response was stored; investigate before any resend.";

    /// <summary>Returns the last committed outcome, or null when the payment does not exist.</summary>
    public async Task<PaymentOutcome?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await payments.FindAsync(paymentId, cancellationToken);
        if (payment is null) return null;
        var run = new Run(payment, unitOfWork, cancellationToken);
        try
        {
            if (payment.MessageType == PaymentMessageTypes.Pacs008 && Acquire(payment) is { } claim)
            {
                await run.CommitAsync();
                await ContinueAsync(run, claim);
            }
        }
        // Another owner or recovery won. This scope is discarded; report what this run committed.
        catch (PersistenceConcurrencyException) { }
        return run.Committed;
    }

    private TransactionClaim? Acquire(OutgoingPayment payment)
    {
        if (payment.CurrentStatus is not (TransactionStatus.Received or TransactionStatus.Sending)) return null;
        var now = Now;
        var claim = work.StageClaim(payment, now, options.Ownership);
        if (claim is not null && payment.CurrentStatus == TransactionStatus.Received) payment.BeginSending(now);
        return claim;
    }

    private async Task ContinueAsync(Run run, TransactionClaim claim)
    {
        var payment = run.Payment;
        var message = await preparation.ReadAsync(payment.Id, run.Token)
            ?? throw new InvalidOperationException("A pacs.008 requires stored protocol identifiers.");
        var submission = await submissions.ReadAsync(payment.Id, run.Token);
        if (submission?.Response is { } stored)
            await InterpretAsync(run, claim, message, stored);
        else if (submission?.Marker is not null)
            await FinishAsync(run, claim, at => payment.MarkOutcomeUnknown(StatusSource.Gateway, at, new(description: SubmittedWithoutResponse)));
        else if (message.Accepted is null)
            await FinishAsync(run, claim, at => payment.RecordNotSent(at, new(description: "Accepted payment data is unavailable; nothing was sent.")));
        else if (!await ExpiredAsync(run, claim, message.Accepted))
            await SubmitAsync(run, claim, message, message.Accepted);
    }

    private async Task SubmitAsync(Run run, TransactionClaim claim, PreparedPaymentMessage message, AcceptedPacs008 accepted)
    {
        var payment = run.Payment;
        var signing = await PrepareAsync(run, claim, message, accepted);
        if (signing is SigningDeferred deferred)
        {
            payment.RecordProcessingFailure(ProcessingStep.Signed, Now, deferred.Reason);
            await ReleaseAsync(run, claim, Now + options.PreparationRetryDelay);
            return;
        }
        if (await ExpiredAsync(run, claim, accepted)) return;

        // Commit the marker before remote I/O: from here on a lost reply is uncertain, never resent.
        var prepared = (SignedMessage)signing;
        if (prepared.Kind == SubmissionMessageKind.DevelopmentUnsigned)
        {
            preparation.StageDevelopmentUnsigned(payment, claim, Now);
            await run.CommitAsync();
        }
        submissions.StageSubmission(payment, claim, prepared.Kind, Now);
        await run.CommitAsync();
        // IPS may act on the message now; its response and the outcome are stored even if the caller stops waiting.
        run.CommitRegardlessOfCancellation();
        IpsSubmissionResponse response;
        try { response = await transport.SendAsync(prepared.Xml, run.Token); }
        catch (Exception exception) when (exception is not OperationCanceledException || !run.Token.IsCancellationRequested)
        {
            await FinishAsync(run, claim, at => payment.MarkOutcomeUnknown(StatusSource.Gateway, at,
                new(description: $"{SubmittedWithoutResponse} {exception.GetType().Name}: {exception.Message}")));
            return;
        }
        submissions.StageResponse(payment, claim, response, Now);
        payment.RecordStep(ProcessingStep.IpsResponded, Now);
        await run.CommitAsync();
        await InterpretAsync(run, claim, message, response);
    }

    // Each artifact is committed before the next step; stored artifacts are reused unchanged.
    private async Task<SigningResult> PrepareAsync(Run run, TransactionClaim claim, PreparedPaymentMessage message, AcceptedPacs008 accepted)
    {
        if (message.SignedXml is { } signed) return new SignedMessage(signed, SubmissionMessageKind.Signed);
        var unsigned = message.UnsignedXml;
        if (unsigned is null)
        {
            unsigned = protocol.BuildUnsignedXml(accepted, message.MessageId, message.TransactionId);
            preparation.StageUnsignedXml(run.Payment, claim, unsigned, Now);
            run.Payment.RecordStep(ProcessingStep.XmlGenerated, Now);
            await run.CommitAsync();
        }
        // The current host policy decides each time whether development may submit unsigned XML.
        var signing = await protocol.SignAsync(unsigned, run.Token);
        if (message.ReadyDisposition == SubmissionMessageKind.DevelopmentUnsigned &&
            signing is not SignedMessage { Kind: SubmissionMessageKind.DevelopmentUnsigned })
            return new SigningDeferred("The frozen development-unsigned message cannot be replaced; current signing policy did not authorize its disposition.");
        if (signing is not SignedMessage { Kind: SubmissionMessageKind.Signed } signature) return signing;
        preparation.StageSignedXml(run.Payment, claim, signature.Xml, Now);
        run.Payment.RecordStep(ProcessingStep.Signed, Now);
        await run.CommitAsync();
        return signature;
    }

    private Task InterpretAsync(Run run, TransactionClaim claim, PreparedPaymentMessage message, IpsSubmissionResponse response)
    {
        var payment = run.Payment;
        if (message.Accepted is null)
            return FinishAsync(run, claim, at => payment.MarkOutcomeUnknown(StatusSource.Gateway, at,
                new(description: "The stored response cannot be correlated without accepted payment data.")));
        var reply = replies.Interpret(response, new(message.MessageId, message.TransactionId, message.Accepted.Payment.EndToEndId));
        submissions.StageInterpretation(payment, claim, reply, Now);
        return FinishAsync(run, claim, at =>
        {
            switch (reply.Status)
            {
                case IpsReplyStatus.Accepted: payment.RecordAcceptance(StatusSource.Ips, at, reply.Details); break;
                case IpsReplyStatus.Rejected: payment.RecordRejection(StatusSource.Ips, at, reply.Details); break;
                default: payment.MarkOutcomeUnknown(StatusSource.Ips, at, reply.Details); break;
            }
        });
    }

    // The deadline only prevents a first send; once a marker exists, expiry cannot establish NotSent.
    private async Task<bool> ExpiredAsync(Run run, TransactionClaim claim, AcceptedPacs008 accepted)
    {
        if (Now <= accepted.SubmissionDeadlineUtc) return false;
        await FinishAsync(run, claim, at => run.Payment.RecordNotSent(at,
            new("TM01", 1015, "Not sent: the submission deadline passed before submission.")));
        return true;
    }

    private Task FinishAsync(Run run, TransactionClaim claim, Action<DateTimeOffset> record)
    {
        record(Now);
        return ReleaseAsync(run, claim, nextActionAtUtc: null);
    }

    // Whatever the run recorded commits together with the ownership release.
    private async Task ReleaseAsync(Run run, TransactionClaim claim, DateTimeOffset? nextActionAtUtc)
    {
        if (!work.StageCompletion(run.Payment, claim, Now, nextActionAtUtc))
            throw new PersistenceConcurrencyException("Ownership expired before the result could be stored.");
        await run.CommitAsync();
    }

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    private sealed class Run(OutgoingPayment payment, IUnitOfWork unitOfWork, CancellationToken token)
    {
        private CancellationToken _commitToken = token;

        public OutgoingPayment Payment => payment;
        public CancellationToken Token { get; } = token;
        public PaymentOutcome Committed { get; private set; } = payment.Current;

        public void CommitRegardlessOfCancellation() => _commitToken = CancellationToken.None;

        public async Task CommitAsync()
        {
            await unitOfWork.SaveAsync(_commitToken);
            Committed = payment.Current;
        }
    }
}
