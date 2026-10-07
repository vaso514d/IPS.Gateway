using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// A refusal recovers like every message type without an investigation; IPS answers it in a header.
// The scheduling rules are covered by the pacs.009 tests, so these cover what is specific to pain.002.
[Collection("Investigation SQL timing")]
public sealed class Pain002RecoveryTests
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(9);

    [Fact]
    public async Task A_flagged_resend_of_the_original_bytes_settles_the_refusal()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Clock.Now += FirstDelay;
        Assert.Contains(h.Id, await h.DueAsync());

        var outcome = (await h.RunAsync())!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Equal(StatusSource.Ips, outcome.Source);
        var resent = Assert.Single(h.Core.Ips.Resent);
        Assert.Equal(h.Core.Ips.Received[0], resent);
        var attempt = await h.ReadAttemptAsync();
        Assert.Null(attempt.InvestigationId);
        Assert.Equal(IpsReplyStatus.Accepted, attempt.Result!.Status);
        Assert.Equal(1, await h.CallbacksAsync());
    }

    [Theory]
    [InlineData("RJCT/1017")]
    [InlineData("RJCT/1009")]
    public async Task A_rejecting_header_on_the_resend_is_final_with_ips_s_reason_and_a_callback(string header)
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Ips.Behavior = (reply, _) => h.Core.Ips.RespondAsync(reply, header);
        h.Core.Clock.Now += FirstDelay;

        var outcome = (await h.RunAsync())!;

        Assert.Equal(TransactionStatus.Rejected, outcome.Status);
        Assert.Equal(StatusSource.Ips, outcome.Source);
        Assert.Contains(header, outcome.Details!.Description);
        Assert.Equal(1, await h.CallbacksAsync());
    }

    [Fact]
    public async Task A_lost_reply_waits_the_backoff_and_resends_the_same_bytes_again()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        h.Core.Clock.Now += FirstDelay;
        Assert.Equal(TransactionStatus.Uncertain, (await h.RunAsync())!.Status);

        h.Core.Ips.Behavior = null;
        h.Core.Clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal(TransactionStatus.Accepted, (await h.RunAsync())!.Status);

        Assert.Equal(2, h.Core.Ips.Resent.Count);
        Assert.All(h.Core.Ips.Resent, xml => Assert.Equal(h.Core.Ips.Received[0], xml));
        Assert.Equal(2, (await h.ReadAttemptAsync()).Number);
    }

    [Fact]
    public async Task The_exact_window_end_prevents_a_resend_and_requires_manual_review()
    {
        await using var h = await Harness.CreateAsync();
        h.Core.Clock.Now = ProcessingHarness.Start + TimeSpan.FromHours(24);

        Assert.Equal(TransactionStatus.ManualReview, (await h.RunAsync())!.Status);

        Assert.Empty(h.Core.Ips.Resent);
        Assert.Equal(1, await h.CallbacksAsync());
    }

    private sealed class Harness(ProcessingHarness core, Guid id) : IAsyncDisposable
    {
        public ProcessingHarness Core { get; } = core;
        public Guid Id { get; } = id;
        public InvestigationOptions Options { get; } = new();

        // The initial send loses its reply, so the refusal is uncertain with an unknown outcome.
        public static async Task<Harness> CreateAsync()
        {
            var core = await ProcessingHarness.CreateAsync();
            core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost outcome", []));
            var id = await core.AcceptPain002Async();
            await core.ProcessAsync(id);
            core.Ips.Behavior = null;
            return new(core, id);
        }

        public async Task<PaymentOutcome?> RunAsync()
        {
            await using var s = Core.Database.Session();
            var policy = new Pacs008SigningPolicy(false, false);
            var protocol = new InvestigationProtocol(new(policy, Core.Clock), Core.Certificates, policy, new([Core.IpsCertificate]));
            return await new OutgoingDuplicateResend(s.Payments, s.Work, new PaymentPreparationRepository(s.Context),
                new ResendRepository(s.Context), s.Unit, protocol, Core.Ips, new IpsReplyInterpreter([Core.IpsCertificate]), Options, Core.Clock)
                .ProcessAsync(Id, default);
        }

        public async Task<IReadOnlyList<Guid>> DueAsync()
        {
            await using var s = Core.Database.Session();
            return await new ResendRepository(s.Context).FindDueAsync(Core.Clock.Now, Options, default);
        }

        public async Task<ResendAttempt> ReadAttemptAsync()
        {
            await using var s = Core.Database.Session();
            return (await new ResendRepository(s.Context).ReadAsync(Id, default))!;
        }

        public async Task<int> CallbacksAsync()
        {
            await using var s = Core.Database.Session();
            return await s.Context.Set<OutgoingStatusDeliveryRow>().CountAsync(row => row.PaymentId == Id);
        }

        public ValueTask DisposeAsync() => Core.DisposeAsync();
    }
}
