using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Xml.Linq;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.Application.Payments.Pain002;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Camt029;
using IPS.Middleware.Infrastructure.Payments.Camt056;
using IPS.Middleware.Infrastructure.Payments.Pacs004;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Payments.Pacs009;
using IPS.Middleware.Infrastructure.Payments.Pain002;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IPS.Middleware.IntegrationTests.Payments;
/// <summary>Real SQL scopes, XML, signing and reply verification around an independent IPS simulator.</summary>
internal sealed class ProcessingHarness : IAsyncDisposable
{
    internal static readonly DateTimeOffset Start = Pacs008Fixture.Created.AddSeconds(1);
    internal static readonly TimeSpan Ownership = TimeSpan.FromSeconds(5);
    private ProcessingHarness(SqlTestDatabase database, bool allowUnsignedInDevelopment)
    {
        Database = database;
        var signer = new Pacs008MessageSigner(new(allowUnsignedInDevelopment, isDevelopment: allowUnsignedInDevelopment), Clock);
        Protocol = new(new Pacs008Preparation(signer, Certificates));
        Pacs009Protocol = new Pacs009Preparation(signer, Certificates);
        Pacs004Protocol = new Pacs004Preparation(signer, Certificates);
        Camt056Protocol = new Camt056Preparation(signer, Certificates);
        Camt029Protocol = new Camt029Preparation(signer, Certificates);
        Pain002Protocol = new Pain002Preparation(signer, Certificates);
        Ips = new(IpsCertificate);
        Certificates.Current = SigningCertificate;
    }

    public SqlTestDatabase Database { get; }
    public TestClock Clock { get; } = new(Start);
    public X509Certificate2 SigningCertificate { get; } = Certificate(Start.AddDays(-1), Start.AddYears(1));
    public X509Certificate2 IpsCertificate { get; } = IpsReplies.Certificate();
    public CertificateSlot Certificates { get; } = new();
    public RecordingPreparation Protocol { get; }
    public Pacs009Preparation Pacs009Protocol { get; }
    public Pacs004Preparation Pacs004Protocol { get; }
    public Camt056Preparation Camt056Protocol { get; }
    public Camt029Preparation Camt029Protocol { get; }
    public Pain002Preparation Pain002Protocol { get; }
    public IpsSimulator Ips { get; }
    public Pacs008Options Options { get; } = new(ownership: Ownership);

    public static async Task<ProcessingHarness> CreateAsync(bool allowUnsignedInDevelopment = false) => new(await SqlTestDatabase.CreateAsync(), allowUnsignedInDevelopment);
    public async Task<PaymentIntakeResult> AcceptAsync(Pacs008Request request, Pacs008ProtocolProfile? profile = null)
    {
        await using var session = Database.Session();
        return await new Pacs008Intake(session.Payments, new OutgoingTransactionIntake(session.Payments, session.Unit, Clock),
                Pacs008Fixture.Policy, profile ?? new("NBGEGE22"), Options, Clock)
            .AcceptAsync(request, JsonSerializer.Serialize(request), default);
    }

    public async Task<PaymentIntakeResult> AcceptAsync(Pacs009Request request)
    {
        await using var session = Database.Session();
        return await new Pacs009Intake(session.Payments, new OutgoingTransactionIntake(session.Payments, session.Unit, Clock),
                Pacs008Fixture.Policy, new("NBGEGE22"), Clock)
            .AcceptAsync(request, JsonSerializer.Serialize(request), default);
    }

    public async Task<PaymentIntakeResult> AcceptAsync(Pacs004Request request)
    {
        await using var session = Database.Session();
        return await new Pacs004Intake(session.Payments, new OutgoingTransactionIntake(session.Payments, session.Unit, Clock),
                Pacs008Fixture.Policy, new("NBGEGE22"), Clock)
            .AcceptAsync(request, JsonSerializer.Serialize(request), default);
    }

    public async Task<PaymentIntakeResult> AcceptAsync(Camt056Request request)
    {
        await using var session = Database.Session();
        return await new Camt056Intake(session.Payments, new OutgoingTransactionIntake(session.Payments, session.Unit, Clock),
                Pacs008Fixture.Policy, new("NBGEGE22"), Clock)
            .AcceptAsync(request, JsonSerializer.Serialize(request), default);
    }

    public async Task<PaymentIntakeResult> AcceptAsync(Camt029Request request)
    {
        await using var session = Database.Session();
        return await new Camt029Intake(session.Payments, new OutgoingTransactionIntake(session.Payments, session.Unit, Clock),
                Pacs008Fixture.Policy, new("NBGEGE22"), Clock)
            .AcceptAsync(request, JsonSerializer.Serialize(request), default);
    }

    public async Task<PaymentIntakeResult> AcceptAsync(Pain002Request request)
    {
        await using var session = Database.Session();
        return await new Pain002Intake(session.Payments, new OutgoingTransactionIntake(session.Payments, session.Unit, Clock),
                Pacs008Fixture.Policy, new("NBGEGE22"), Clock)
            .AcceptAsync(request, JsonSerializer.Serialize(request), default);
    }

    public async Task<Guid> AcceptPain002Async(string reference = "processing") =>
        (await AcceptAsync(Pain002Fixture.Request(reference))).Intake!.Payment.Id;

    public async Task<Guid> AcceptCamt029Async(string reference = "processing") =>
        (await AcceptAsync(Camt029Fixture.Request(reference))).Intake!.Payment.Id;

    public async Task<Guid> AcceptCamt056Async(string reference = "processing") =>
        (await AcceptAsync(Camt056Fixture.Request(reference))).Intake!.Payment.Id;

    public async Task<Guid> AcceptPacs004Async(string reference = "processing") =>
        (await AcceptAsync(Pacs004Fixture.Request(reference))).Intake!.Payment.Id;

    public async Task<Guid> AcceptPacs009Async(string reference = "processing") =>
        (await AcceptAsync(Pacs009Fixture.Request(reference))).Intake!.Payment.Id;

    public async Task<Guid> AcceptAsync(string reference = "processing") => (await AcceptAsync(Pacs008Fixture.Request() with { ClientReference = reference })).Intake!.Payment.Id;
    public async Task<PaymentOutcome?> ProcessAsync(Guid id, CancellationToken cancellationToken = default, params IInterceptor[] interceptors)
    {
        await using var session = Database.Session(interceptors);
        return await new OutgoingPaymentProcessing(session.Payments, session.Work, new PaymentPreparationRepository(session.Context),
                session.Submissions, session.Unit, [Protocol, Pacs009Protocol, Pacs004Protocol, Camt056Protocol, Camt029Protocol, Pain002Protocol], Ips, new IpsReplyInterpreter([IpsCertificate]), Options, Clock)
            .ProcessAsync(id, cancellationToken);
    }

    public async Task<TransactionWorkResult> RecoverAsync(Guid id)
    {
        await using var session = Database.Session();
        return await session.Processing(Clock.GetUtcNow()).TryRecoverAsync(id, default);
    }

    public async Task<IReadOnlyList<Guid>> FindDueAsync(TransactionStatus status, DateTimeOffset at)
    {
        await using var session = Database.Session();
        return await session.Work.FindDueAsync(status, at, 10, default);
    }

    public async Task<StoredPayment> ReadAsync(Guid id)
    {
        await using var session = Database.Session();
        var payment = (await session.Payments.FindAsync(id, default))!;
        var metadata = session.Context.Metadata(payment);
        return new(payment, (await new PaymentPreparationRepository(session.Context).ReadAsync(id, default))!,
            (await session.Submissions.ReadAsync(id, default))!,
            (await session.Payments.ReadEventsAsync(id, default)).Select(e => e.Name).ToArray(),
            metadata.ClaimToken, metadata.NextActionAtUtc,
            metadata.AcceptedJson, (await session.Payments.ReadRequestAsync(id, default))!);
    }

    internal static X509Certificate2 Certificate(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Participant signing", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    public async ValueTask DisposeAsync()
    {
        SigningCertificate.Dispose();
        IpsCertificate.Dispose();
        await Database.DisposeAsync();
    }
}

internal sealed record StoredPayment(OutgoingPayment Payment, PreparedPaymentMessage Message, PaymentSubmission Submission, IReadOnlyList<string> Events, Guid? ClaimToken, DateTimeOffset? NextActionAtUtc, string? AcceptedJson, string RequestJson);
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class CertificateSlot : ISigningCertificateSource
{
    public X509Certificate2? Current { get; set; }
    public bool Unavailable { get; set; }

    public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken cancellationToken) => Unavailable ? throw new IOException("The certificate store is unavailable.") : ValueTask.FromResult(Current);
}

/// <summary>Counts protocol work so tests can prove committed artifacts are reused.</summary>
internal sealed class RecordingPreparation(IOutgoingMessageProtocol inner) : IOutgoingMessageProtocol
{
    public string MessageType => inner.MessageType;
    public int Builds { get; private set; }
    public int Signs { get; private set; }
    public Action? BeforeSign { get; set; }

    public string BuildUnsignedXml(IAcceptedPayment accepted, string messageId, string transactionId)
    {
        Builds++;
        return inner.BuildUnsignedXml(accepted, messageId, transactionId);
    }

    public Task<SigningResult> SignAsync(string unsignedXml, CancellationToken cancellationToken)
    {
        Signs++;
        BeforeSign?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return inner.SignAsync(unsignedXml, cancellationToken);
    }
}

/// <summary>Reads sent references without production mapping code and replies with Java-signed pacs.002 fixtures.</summary>
internal sealed class IpsSimulator(X509Certificate2 ipsCertificate) : IIpsTransport
{
    private readonly List<string> _received = [];
    private readonly List<string> _resent = [];
    public IReadOnlyList<string> Received
    {
        get
        {
            lock (_received)
            {
                return _received.ToArray();
            }
        }
    }

    public Func<IpsReplies.Reply, CancellationToken, Task<IpsSubmissionResponse>>? Behavior { get; set; }
    public TaskCompletionSource? Gate { get; set; }

    // Flagged resends, in the order IPS received them.
    public IReadOnlyList<string> Resent
    {
        get
        {
            lock (_received)
            {
                return _resent.ToArray();
            }
        }
    }

    public Task<IpsSubmissionResponse> ResendAsync(string xml, CancellationToken cancellationToken)
    {
        lock (_received)
        {
            _resent.Add(xml);
        }

        return SendAsync(xml, cancellationToken);
    }

    public async Task<IpsSubmissionResponse> SendAsync(string xml, CancellationToken cancellationToken)
    {
        lock (_received)
        {
            _received.Add(xml);
        }

        var document = XDocument.Parse(xml);
        // A return is answered with the original payment's ids; every other message with its own.
        string Value(params string[] names) => document.Descendants().First(element => names.Contains(element.Name.LocalName)).Value;
        var reply = new IpsReplies.Reply
        {
            MessageId = Value("BizMsgIdr"),
            TransactionId = Value("OrgnlTxId", "TxId", "OrgnlPmtInfId"),
            EndToEndId = Value("OrgnlEndToEndId", "EndToEndId", "OrgnlPmtInfId"),
            OriginalMessageName = Value("MsgDefIdr")
        };
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return Behavior is { } behavior ? await behavior(reply, cancellationToken) : await RespondAsync(reply, "ACCP");
    }

    public async Task<IpsSubmissionResponse> RespondAsync(IpsReplies.Reply reply, string requestStatus) => new(200, (await IpsReplies.SignAsync(ipsCertificate, IpsReplies.Unsigned(reply)))[0],
            [new("X-MONTRAN-IPS-ReqSts", requestStatus), new("X-MONTRAN-IPS-MessageType", "pacs.002")]);
}

internal sealed class SimulatedCrash : Exception;
/// <summary>Terminates a save that would commit the selected change, rolling back its transaction.</summary>
internal sealed class CrashOnSave(Func<EntityEntry<OutgoingPayment>, bool> when) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => eventData.Context!.ChangeTracker.Entries<OutgoingPayment>().Any(when) ? throw new SimulatedCrash() : ValueTask.FromResult(result);
}
