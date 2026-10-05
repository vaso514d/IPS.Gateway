using System.Data.Common;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.ProcessingHarness;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class Pacs008ProcessingTests
{
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace Pacs = Pacs008Xml.DocumentNamespace;
    [Fact]
    public async Task Accepted_payment_is_prepared_submitted_once_and_final_outcome_is_returned_unchanged()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        var outcome = await harness.ProcessAsync(id);
        Assert.Equal(TransactionStatus.Accepted, outcome!.Status);
        Assert.Equal(StatusSource.Ips, outcome.Source);
        var stored = await harness.ReadAsync(id);
        Assert.Equal(stored.Message.SignedXml, Assert.Single(harness.Ips.Received));
        Assert.Equal(SubmissionMessageKind.Signed, stored.Submission.Marker!.MessageKind);
        Assert.Equal(200, stored.Submission.Response!.HttpStatusCode);
        Assert.Null(stored.ClaimToken);
        Assert.Equal(["payment.received", "payment.sending-started", "payment.processing-observed", "payment.processing-observed",
            "payment.processing-observed", "payment.accepted"], stored.Events);
        harness.Clock.Now = Start.AddMinutes(1);
        Assert.Equal(outcome, await harness.ProcessAsync(id));
        Assert.Single(harness.Ips.Received);
        Assert.Equal(stored.Events, (await harness.ReadAsync(id)).Events);
        Assert.Null(await harness.ProcessAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Valid_rejection_records_the_source_reason_details()
    {
        await using var harness = await CreateAsync();
        harness.Ips.Behavior = (reply, _) => harness.Ips.RespondAsync(reply with
        {
            GroupStatus = "RJCT",
            TransactionStatus = "RJCT",
            ReasonCode = "AC01",
            AdditionalInformation = "The creditor IBAN code is invalid."
        }, "RJCT/1009");
        var outcome = await harness.ProcessAsync(await harness.AcceptAsync());
        Assert.Equal(TransactionStatus.Rejected, outcome!.Status);
        Assert.Equal(new PaymentDetails("AC01", 1009, "The creditor IBAN code is invalid."), outcome.Details);
    }

    [Fact]
    public async Task A_reply_lost_after_ips_processed_the_payment_is_uncertain_and_never_resent()
    {
        await using var harness = await CreateAsync();
        harness.Ips.Behavior = (_, _) => throw new HttpRequestException("The connection closed after IPS processed the payment.");
        var id = await harness.AcceptAsync();
        Assert.Equal(TransactionStatus.Uncertain, (await harness.ProcessAsync(id))!.Status);
        var stored = await harness.ReadAsync(id);
        Assert.NotNull(stored.Submission.Marker);
        Assert.Null(stored.Submission.Response);
        Assert.Null(stored.ClaimToken);
        Assert.Contains(id, await harness.FindDueAsync(TransactionStatus.Uncertain, harness.Clock.Now));
        harness.Clock.Now += Ownership;
        Assert.Equal(TransactionStatus.Uncertain, (await harness.ProcessAsync(id))!.Status);
        Assert.Equal(TransactionWorkResult.Unchanged, await harness.RecoverAsync(id));
        Assert.Single(harness.Ips.Received);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("timeout")]
    public async Task Transport_failure_after_the_marker_is_uncertain_never_not_sent(string failure)
    {
        await using var harness = await CreateAsync();
        harness.Ips.Behavior = (_, _) => failure == "error"
            ? throw new HttpRequestException("Connection refused.")
            : throw new TaskCanceledException("The IPS call timed out.");
        var id = await harness.AcceptAsync();
        var outcome = (await harness.ProcessAsync(id))!;
        Assert.Equal(TransactionStatus.Uncertain, outcome.Status);
        Assert.Contains("investigate", outcome.Details.Description);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("unsigned")]
    [InlineData("signed")]
    [InlineData("response")]
    public async Task Restart_after_each_checkpoint_resumes_without_regenerating_committed_artifacts(string checkpoint)
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        // Terminate the save that would commit the step after the checkpoint.
        var crash = new CrashOnSave(entry => checkpoint switch
        {
            "accepted" => entry.Context.ChangeTracker.Entries<OutgoingPaymentMetadata>().Any(p => p.Property(r => r.UnsignedXml).IsModified),
            "unsigned" => entry.Context.ChangeTracker.Entries<OutgoingMessageRow>().Any(p => p.State == EntityState.Added && p.Entity.Direction == OutgoingMessageDirection.Outbound),
            "signed" => entry.Context.ChangeTracker.Entries<OutgoingMessageRow>().Any(p => p.Entity.Status == MessageJournalStatus.SendStarted && p.Property(r => r.Status).IsModified),
            _ => entry.Entity.IsFinal && entry.Property(nameof(OutgoingPayment.CurrentStatus)).IsModified
        });
        await Assert.ThrowsAsync<SimulatedCrash>(() => harness.ProcessAsync(id, default, crash));
        var crashed = await harness.ReadAsync(id);
        Assert.Equal(TransactionStatus.Sending, crashed.Payment.CurrentStatus);
        Assert.NotNull(crashed.ClaimToken);
        Assert.Equal(checkpoint == "accepted", crashed.Message.UnsignedXml is null);
        Assert.Equal(checkpoint is "signed" or "response", crashed.Message.SignedXml is not null);
        Assert.Equal(checkpoint == "response", crashed.Submission.Response is not null);
        Assert.Equal(TransactionWorkResult.Unchanged, await harness.RecoverAsync(id));
        harness.Clock.Now += Ownership;
        Assert.Equal(TransactionWorkResult.Saved, await harness.RecoverAsync(id));
        Assert.Equal(TransactionStatus.Sending, (await harness.ReadAsync(id)).Payment.CurrentStatus);
        Assert.Contains(id, await harness.FindDueAsync(TransactionStatus.Sending, harness.Clock.Now));
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id))!.Status);
        var resumed = await harness.ReadAsync(id);
        Assert.Equal(crashed.Message.UnsignedXml ?? resumed.Message.UnsignedXml, resumed.Message.UnsignedXml);
        Assert.Equal(crashed.Message.SignedXml ?? resumed.Message.SignedXml, resumed.Message.SignedXml);
        Assert.Equal(checkpoint == "accepted" ? 2 : 1, harness.Protocol.Builds);
        Assert.Equal(checkpoint is "accepted" or "signed" or "response" ? 1 : 2, harness.Protocol.Signs);
        Assert.Equal(resumed.Message.SignedXml, Assert.Single(harness.Ips.Received));
    }

    [Fact]
    public async Task Competing_executions_send_once_and_a_live_owner_excludes_others()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        harness.Ips.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = harness.ProcessAsync(id);
        while (harness.Ips.Received.Count == 0)
        {
            await Task.Delay(10);
        }

        var excluded = await harness.ProcessAsync(id);
        Assert.Equal(TransactionStatus.Sending, excluded!.Status);
        harness.Ips.Gate.SetResult();
        Assert.Equal(TransactionStatus.Accepted, (await owner)!.Status);
        Assert.Single(harness.Ips.Received);
        var competing = await harness.AcceptAsync("competing");
        harness.Ips.Gate = null;
        var outcomes = await Task.WhenAll(harness.ProcessAsync(competing), harness.ProcessAsync(competing));
        Assert.Contains(outcomes, outcome => outcome!.Status == TransactionStatus.Accepted);
        Assert.Equal(2, harness.Ips.Received.Count);
    }

    [Fact]
    public async Task A_stale_owner_cannot_store_a_late_response_after_recovery()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        harness.Ips.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = harness.ProcessAsync(id);
        while (harness.Ips.Received.Count == 0)
        {
            await Task.Delay(10);
        }

        harness.Clock.Now += Ownership;
        Assert.Equal(TransactionWorkResult.Saved, await harness.RecoverAsync(id));
        harness.Ips.Gate.SetResult();
        Assert.Equal(TransactionStatus.Sending, (await stale)!.Status);
        var stored = await harness.ReadAsync(id);
        Assert.Equal(TransactionStatus.Uncertain, stored.Payment.CurrentStatus);
        Assert.NotNull(stored.Submission.Marker);
        Assert.Null(stored.Submission.Response);
        Assert.Single(harness.Ips.Received);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("unavailable")]
    public async Task Certificate_failure_defers_preparation_keeps_artifacts_and_retries_safely(string failure)
    {
        await using var harness = await CreateAsync();
        using var expired = Certificate(Start.AddDays(-2), Start.AddDays(-1));
        harness.Certificates.Current = failure switch
        {
            "missing" => null,
            "expired" => expired,
            _ => harness.SigningCertificate
        };
        harness.Certificates.Unavailable = failure == "unavailable";
        var id = await harness.AcceptAsync();
        Assert.Equal(TransactionStatus.Sending, (await harness.ProcessAsync(id))!.Status);
        var deferred = await harness.ReadAsync(id);
        Assert.NotNull(deferred.Message.UnsignedXml);
        Assert.Null(deferred.Message.SignedXml);
        Assert.Null(deferred.Submission.Marker);
        Assert.Null(deferred.ClaimToken);
        Assert.Equal(Start.AddSeconds(1), deferred.NextActionAtUtc);
        Assert.Equal("payment.processing-failed", deferred.Events[^1]);
        Assert.Empty(await harness.FindDueAsync(TransactionStatus.Sending, Start));
        Assert.Equal(TransactionStatus.Sending, (await harness.ProcessAsync(id))!.Status);
        Assert.Equal(1, harness.Protocol.Signs);
        harness.Certificates.Current = harness.SigningCertificate;
        harness.Certificates.Unavailable = false;
        harness.Clock.Now = Start.AddSeconds(1);
        Assert.Contains(id, await harness.FindDueAsync(TransactionStatus.Sending, harness.Clock.Now));
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id))!.Status);
        Assert.Equal(deferred.Message.UnsignedXml, (await harness.ReadAsync(id)).Message.UnsignedXml);
        Assert.Equal(1, harness.Protocol.Builds);
        Assert.Single(harness.Ips.Received);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deadline_expiry_before_submission_records_not_sent(bool afterCertificateRetries)
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        if (afterCertificateRetries)
        {
            harness.Certificates.Current = null;
            Assert.Equal(TransactionStatus.Sending, (await harness.ProcessAsync(id))!.Status);
        }

        harness.Clock.Now = Pacs008Fixture.Request().AcceptanceDateTime!.Value.AddSeconds(20).AddTicks(1);
        var outcome = (await harness.ProcessAsync(id))!;
        Assert.Equal(TransactionStatus.NotSent, outcome.Status);
        Assert.Equal(("TM01", (int?)1015), (outcome.Details.ReasonCode, outcome.Details.IpsInternalCode));
        var stored = await harness.ReadAsync(id);
        Assert.Null(stored.Submission.Marker);
        Assert.Null(stored.ClaimToken);
        Assert.Empty(harness.Ips.Received);
        Assert.Equal(afterCertificateRetries ? 1 : 0, harness.Protocol.Builds);
    }

    [Fact]
    public async Task Development_unsigned_submission_is_gated_by_host_policy_and_never_fills_signed_xml()
    {
        await using var harness = await CreateAsync(allowUnsignedInDevelopment: true);
        harness.Certificates.Current = null;
        var id = await harness.AcceptAsync();
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id))!.Status);
        var stored = await harness.ReadAsync(id);
        Assert.Equal(SubmissionMessageKind.DevelopmentUnsigned, stored.Submission.Marker!.MessageKind);
        Assert.Null(stored.Message.SignedXml);
        Assert.Equal(stored.Message.UnsignedXml, Assert.Single(harness.Ips.Received));
    }

    [Fact]
    public async Task Cancellation_before_submission_keeps_preparation_resumable()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        using var cancellation = new CancellationTokenSource();
        harness.Protocol.BeforeSign = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.ProcessAsync(id, cancellation.Token));
        harness.Protocol.BeforeSign = null;
        var cancelled = await harness.ReadAsync(id);
        Assert.NotNull(cancelled.Message.UnsignedXml);
        Assert.Null(cancelled.Submission.Marker);
        harness.Clock.Now += Ownership;
        Assert.Equal(TransactionWorkResult.Saved, await harness.RecoverAsync(id));
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id))!.Status);
        Assert.Equal(1, harness.Protocol.Builds);
        Assert.Single(harness.Ips.Received);
    }

    [Fact]
    public async Task Cancellation_after_the_marker_leaves_uncertainty_for_recovery()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        using var cancellation = new CancellationTokenSource();
        harness.Ips.Behavior = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unreachable.");
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.ProcessAsync(id, cancellation.Token));
        Assert.NotNull((await harness.ReadAsync(id)).Submission.Marker);
        harness.Clock.Now += Ownership;
        Assert.Equal(TransactionWorkResult.Saved, await harness.RecoverAsync(id));
        Assert.Equal(TransactionStatus.Uncertain, (await harness.ProcessAsync(id))!.Status);
        Assert.Single(harness.Ips.Received);
    }

    [Fact]
    public async Task Accepted_snapshot_fixes_payment_data_and_mapping_settings_at_intake()
    {
        await using var harness = await CreateAsync();
        var request = Pacs008Fixture.Request() with
        {
            ClientReference = "snapshot",
            CategoryPurposeCode = "cash",
            UltimateDebtor = new()
            {
                Type = 0,
                Name = "Ultimate debtor",
                Identifier = "UD-1"
            },
            UltimateCreditor = new()
            {
                Type = 1,
                Name = "Ultimate creditor"
            },
            Debtor = Pacs008Fixture.Request().Debtor! with
            {
                Identifier = "01001000001",
                BillIdentifier = "BILL-1",
                IndirectParticipantBic = "MEMBER-1",
                Address = new() { StreetName = "Rustaveli", TownName = "Tbilisi", Country = "GE", AddressLines = "Line one" }
            },
            PaymentInitiation = new()
            {
                ChannelCode = "WEB",
                Geolocation = ["41.7", "44.8"]
            },
            InitiationChannelInstrument = new()
            {
                ChannelCode = "MOBL",
                InstrumentCodes = ["CARD", "PRXY"],
                ElectronicAddress = "41.7,44.8"
            },
            Remittance = new()
            {
                Unstructured = "Invoice 42",
                Structured = [new()
                {
                    ReferenceType = "scor",
                    Reference = "RF18",
                    ReferenceIssuer = "Issuer"
                }

                ]
            }
        };
        var profile = new Pacs008ProtocolProfile("NBGEGE22", "SEPA", RemittanceDeliveryMethod.Email);
        var intake = await harness.AcceptAsync(request, profile);
        Assert.True(intake.Intake is not null, string.Join("; ", intake.Errors.Select(error => $"{error.Field}: {error.Message}")));
        var id = intake.Intake.Payment.Id;
        var message = (await harness.ReadAsync(id)).Message;
        var accepted = message.Accepted!;
        // XML from the in-memory validated payment must equal XML from the restored snapshot.
        var expected = new Pacs008Xml(profile).Build(ValidatedPacs008.Validate(request, Pacs008Fixture.Policy).Payment!,
            new(message.MessageId, message.TransactionId, Start));
        // Reloaded JSON must preserve the same immutable boundary as freshly validated input.
        var restored = accepted.Payment;
        Assert.Throws<NotSupportedException>(() => ((IList<string>)restored.PaymentInitiation!.Geolocation)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)restored.InitiationChannel!.InstrumentCodes)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<PaymentRemittanceReference>)restored.Remittance!.Structured)[0] =
            new("SCOR", "changed", null, null));
        Assert.Equal(expected, new Pacs008Xml(accepted.Profile).Build(restored,
            new(message.MessageId, message.TransactionId, accepted.EnvelopeCreatedAtUtc)));
        // Processing has no access to current policy or mapping settings, and runs later than intake.
        harness.Clock.Now = Start.AddSeconds(3);
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id))!.Status);
        var stored = await harness.ReadAsync(id);
        Assert.Equal(expected, stored.Message.UnsignedXml);
        var xml = XDocument.Parse(stored.Message.UnsignedXml!);
        Assert.Equal("SEPA", xml.Descendants(Pacs + "SvcLvl").Single().Value);
        Assert.Equal("EMAL", xml.Descendants(Pacs + "Mtd").First().Value);
        Assert.Equal("2026-10-04T00:00:00.0000000Z", xml.Descendants(Head + "CreDt").Single().Value);
        Assert.Equal(request.AcceptanceDateTime!.Value.AddSeconds(20), accepted.SubmissionDeadlineUtc);
        Assert.Contains("\"version\":1", stored.AcceptedJson);
        Assert.Contains("\"remittanceMethod\":\"Email\"", stored.AcceptedJson);
        Assert.DoesNotContain("$type", stored.AcceptedJson);
    }

    [Fact]
    public async Task Duplicate_intake_returns_the_original_without_replacing_or_processing_and_invalid_input_stores_nothing()
    {
        await using var harness = await CreateAsync();
        var first = await harness.AcceptAsync(Pacs008Fixture.Request() with { ClientReference = "duplicate" });
        var original = await harness.ReadAsync(first.Intake!.Payment.Id);
        harness.Clock.Now = Start.AddSeconds(2);
        // A retry is recognised before validation, so even a body current policy rejects returns the stored payment.
        var duplicate = await harness.AcceptAsync(Pacs008Fixture.Request() with { ClientReference = " duplicate ", Amount = -1m }, new("NBGEGE22", "SEPA"));
        Assert.False(duplicate.Intake!.Created);
        Assert.Equal(first.Intake.Payment.Id, duplicate.Intake.Payment.Id);
        var stored = await harness.ReadAsync(first.Intake.Payment.Id);
        Assert.Equal(original.AcceptedJson, stored.AcceptedJson);
        Assert.Equal(original.RequestJson, stored.RequestJson);
        Assert.Equal((original.Message.MessageId, original.Message.TransactionId), (stored.Message.MessageId, stored.Message.TransactionId));
        Assert.Equal(["payment.received"], stored.Events);
        Assert.Empty(harness.Ips.Received);
        var invalid = await harness.AcceptAsync(Pacs008Fixture.Request() with { ClientReference = "invalid", Amount = -1m });
        Assert.Null(invalid.Intake);
        Assert.Contains(invalid.Errors, error => error.Field == "amount");
    }

    [Fact]
    public async Task A_response_received_before_cancellation_is_stored_and_interpreted()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        using var cancellation = new CancellationTokenSource();
        harness.Ips.Behavior = async (reply, _) =>
        {
            var response = await harness.Ips.RespondAsync(reply, "ACCP");
            cancellation.Cancel();
            return response;
        };
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id, cancellation.Token))!.Status);
        Assert.NotNull((await harness.ReadAsync(id)).Submission.Response);
    }

    [Fact]
    public async Task Stored_snapshot_json_is_the_pinned_version_1_format()
    {
        await using var harness = await CreateAsync();
        var stored = await harness.ReadAsync(await harness.AcceptAsync());
        Assert.Equal(PinnedSnapshot, stored.AcceptedJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expired_evidence_budget_leaves_committed_checkpoints_recoverable(bool afterResponse)
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        var delay = new DelayEvidenceSave(afterResponse);
        harness.Ips.Behavior = async (reply, _) =>
        {
            var response = await harness.Ips.RespondAsync(reply, "ACCP");
            delay.Armed = true;
            return response;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.ProcessAsync(id, default, delay));
        Assert.True(delay.Triggered);
        var stored = await harness.ReadAsync(id);
        Assert.NotNull(stored.Submission.Marker);
        Assert.Equal(afterResponse, stored.Submission.Response is not null);
        harness.Clock.Now += Ownership;
        await harness.RecoverAsync(id);
        Assert.Equal(afterResponse ? TransactionStatus.Accepted : TransactionStatus.Uncertain, (await harness.ProcessAsync(id))!.Status);
        Assert.Single(harness.Ips.Received);
    }

    [Fact]
    public async Task Evidence_budget_starts_after_the_remote_exchange_not_before_it()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        harness.Ips.Behavior = async (reply, token) =>
        {
            await Task.Delay(harness.Options.PersistenceBudget + TimeSpan.FromMilliseconds(100), token);
            return await harness.Ips.RespondAsync(reply, "ACCP");
        };
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id))!.Status);
        Assert.NotNull((await harness.ReadAsync(id)).Submission.Response);
        Assert.Single(harness.Ips.Received);
    }

    private sealed class DelayEvidenceSave(bool afterResponse) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && (!afterResponse || eventData.Context!.ChangeTracker.Entries<OutgoingPayment>().Any(e => e.Entity.CurrentStatus == TransactionStatus.Accepted)))
            {
                Triggered = true;
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return result;
        }
    }

    // Changing this format requires a new snapshot version and a reader for version 1.
    private const string PinnedSnapshot = """
        {"version":1,"accepted":{"payment":{"clientReference":"processing","instructionId":"BANK-1","endToEndId":"E2E-1","creationDateTime":"2026-10-03T23:59:59+00:00","acceptanceDateTime":"2026-10-03T23:59:59.5+00:00","amount":12.34567,"currency":"GEL","priority":"High","categoryPurposeCode":null,"participantBic":"BAGAGE22","debtor":{"kind":"Individual","name":"\u10E5\u10D0\u10E0\u10D7\u10E3\u10DA\u10D8 \u0026 Debtor","identifier":null,"billIdentifier":null,"address":null},"creditor":{"kind":"Individual","name":"Creditor","identifier":null,"billIdentifier":null,"address":null},"debtorAccount":{"value":"GE95TB0000000123456789","kind":"Iban"},"creditorAccount":{"value":"GE29NB0000000101904917","kind":"Iban"},"debtorAgent":{"bic":"BAGAGE22","indirectParticipant":null},"creditorAgent":{"bic":"TBCBGE22","indirectParticipant":null},"ultimateDebtor":null,"ultimateCreditor":null,"paymentInitiation":null,"initiationChannel":null,"remittance":null},"profile":{"ipsBic":"NBGEGE22","serviceLevelCode":"INST","remittanceMethod":"Uri"},"envelopeCreatedAtUtc":"2026-10-04T00:00:00+00:00","submissionDeadlineUtc":"2026-10-04T00:00:19.5+00:00"}}
        """;
}
