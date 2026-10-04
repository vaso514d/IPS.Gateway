using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests;

public sealed class PaymentSettingsTests
{
    [Fact]
    public void Host_binds_overrides_and_freezes_validated_typed_settings()
    {
        using var factory = new SettingsFactory(new()
        {
            ["Payments:Outgoing:Pacs008:SubmissionWindow"] = "00:00:12",
            ["Payments:Outgoing:Pacs008:Ownership"] = "00:00:35",
            ["Payments:Outgoing:Pacs008:PreparationRetryDelay"] = "00:00:04",
            ["Payments:Incoming:Processing:PaymentWindow"] = "00:00:18",
            ["Payments:Incoming:Processing:StatusBudget"] = "00:00:04",
            ["Payments:Incoming:Processing:ReplyReserve"] = "00:00:03",
            ["Payments:Incoming:Processing:Ownership"] = "00:00:40",
            ["Payments:Incoming:Processing:PersistenceBudget"] = "00:00:01",
            ["Payments:Incoming:Processing:FollowUpDelay"] = "00:00:07",
            ["Payments:Incoming:Reconciliation:CallTimeout"] = "00:00:06",
            ["Payments:Incoming:Reconciliation:PersistenceBudget"] = "00:00:01",
            ["Payments:Incoming:Reconciliation:Ownership"] = "00:00:30",
            ["Payments:Incoming:Reconciliation:Window"] = "02:00:00",
            ["Payments:Incoming:Reconciliation:DiscoveryBatch"] = "9",
            ["Payments:Incoming:Reconciliation:RetryDelays:0"] = "00:00:11",
            ["Payments:Incoming:Reconciliation:RepeatInterval"] = "00:09:00",
            ["Payments:Incoming:Scheduling:Capacity"] = "12",
            ["Payments:Incoming:Scheduling:DiscoveryBatch"] = "5",
            ["Payments:Incoming:Scheduling:DiscoveryInterval"] = "00:00:02",
            ["Payments:Incoming:Scheduling:ClaimDuration"] = "00:00:50",
            ["Payments:Incoming:Scheduling:RegistrationMaxAttempts"] = "3"
        });
        var services = factory.Services;
        var outgoing = services.GetRequiredService<Pacs008Options>();
        Assert.Equal((12d, 35d, 4d), (outgoing.SubmissionWindow.TotalSeconds, outgoing.Ownership.TotalSeconds, outgoing.PreparationRetryDelay.TotalSeconds));
        var processing = services.GetRequiredService<IncomingProcessingOptions>();
        Assert.Equal((18d, 4d, 3d, 40d, 1d, 7d), (processing.PaymentWindow.TotalSeconds, processing.StatusBudget.TotalSeconds,
            processing.ReplyReserve.TotalSeconds, processing.Ownership.TotalSeconds, processing.PersistenceBudget.TotalSeconds, processing.FollowUpDelay.TotalSeconds));
        var reconciliation = services.GetRequiredService<IncomingReconciliationOptions>();
        Assert.Equal((6d, 1d, 30d, 2d, 9), (reconciliation.CallTimeout.TotalSeconds, reconciliation.PersistenceBudget.TotalSeconds,
            reconciliation.Ownership.TotalSeconds, reconciliation.Window.TotalHours, reconciliation.DiscoveryBatch));
        Assert.Equal(TimeSpan.FromSeconds(11), reconciliation.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(9), reconciliation.RetryDelay(4));
        var scheduling = services.GetRequiredService<InboundSchedulingOptions>();
        Assert.Equal((12, 5, 2d, 50d, 3), (scheduling.Capacity, scheduling.DiscoveryBatch, scheduling.DiscoveryInterval.TotalSeconds,
            scheduling.ClaimDuration.TotalSeconds, scheduling.RegistrationMaxAttempts));
        services.GetRequiredService<IConfiguration>()["Payments:Incoming:Processing:FollowUpDelay"] = "00:00:99";
        Assert.Equal(TimeSpan.FromSeconds(7), services.GetRequiredService<IncomingProcessingOptions>().FollowUpDelay);
        using var configured = new ServiceCollection().AddSingleton(scheduling).AddSingleton(processing).AddInboundFoundations().BuildServiceProvider();
        Assert.Same(scheduling, configured.GetRequiredService<InboundSchedulingOptions>());
        Assert.Same(processing, configured.GetRequiredService<IncomingProcessingOptions>());
    }

    [Theory]
    [InlineData("Payments:Outgoing:Pacs008:PreparationRetryDelay", "00:00:00")]
    [InlineData("Payments:Incoming:Processing:StatusBudget", "00:00:19")]
    [InlineData("Payments:Incoming:Processing:FollowUpDelay", "00:00:00")]
    [InlineData("Payments:Incoming:Processing:Ownership", "00:00:22")]
    [InlineData("Payments:Incoming:Reconciliation:Ownership", "00:00:22")]
    [InlineData("Payments:Incoming:Reconciliation:RetryDelays:1", "00:00:00")]
    [InlineData("Payments:Incoming:Reconciliation:RepeatInterval", "-00:00:01")]
    [InlineData("Payments:Incoming:Reconciliation:Window", "00:00:00")]
    [InlineData("Payments:Incoming:Reconciliation:DiscoveryBatch", "0")]
    [InlineData("Payments:Incoming:Scheduling:Capacity", "1")]
    [InlineData("Payments:Incoming:Scheduling:RegistrationMaxAttempts", "0")]
    [InlineData("Payments:Incoming:Scheduling:ClaimDuration", "invalid")]
    [InlineData("Payments:Incoming:Reconciliation:RetryUnsafeRequests", "true")]
    public void Invalid_configuration_fails_before_serving_requests(string key, string value)
    {
        using var factory = new SettingsFactory(new() { [key] = value });
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Retry_schedule_defensively_copies_configured_values()
    {
        var values = new[] { TimeSpan.FromSeconds(2) };
        var options = new IncomingReconciliationOptions(retryDelays: values, repeatInterval: TimeSpan.FromSeconds(9));
        values[0] = TimeSpan.Zero;
        Assert.Equal(TimeSpan.FromSeconds(2), options.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(9), options.RetryDelay(2));
        Assert.Throws<NotSupportedException>(() => ((IList<TimeSpan>)options.RetryDelays)[0] = TimeSpan.Zero);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Registration_obeys_configured_retry_limit_with_a_fresh_scope_for_each_attempt(int attempts)
    {
        var scopes = new List<Guid>();
        using var services = new ServiceCollection()
            .AddSingleton(scopes)
            .AddScoped<IInboundReceiptRepository, ConflictingReceiptRepository>()
            .AddScoped<IUnitOfWork, UnusedUnitOfWork>()
            .AddInboundFoundations(new(registrationMaxAttempts: attempts))
            .BuildServiceProvider();
        var registration = services.GetRequiredService<InboundReceiptRegistration>();
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => registration.RegisterAsync(
            new("BAGAGE22", 1, "pacs.008", "<fixture/>", false, DateTimeOffset.UnixEpoch), default));
        Assert.Equal(attempts, scopes.Count);
        Assert.Equal(scopes.Count, scopes.Distinct().Count());
    }

    [Fact]
    public void Empty_retry_sequence_in_json_uses_repeat_interval()
    {
        using var factory = new EmptyRetryFactory();
        var options = factory.Services.GetRequiredService<IncomingReconciliationOptions>();
        Assert.Empty(options.RetryDelays);
        Assert.Equal(options.RepeatInterval, options.RetryDelay(1));
    }

    private sealed class EmptyRetryFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
                {"Payments":{"Incoming":{"Reconciliation":{"RetryDelays":[]}}}}
                """)));
        });
    }

    private sealed class SettingsFactory(Dictionary<string, string?> values) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(values));
    }
    private sealed class ConflictingReceiptRepository(List<Guid> attempts) : IInboundReceiptRepository
    {
        private readonly Guid _scope = Guid.NewGuid();
        public Task<InboundRegistration> StageRegistrationAsync(InboundReceipt receipt, CancellationToken token)
        {
            attempts.Add(_scope);
            throw new PersistenceConcurrencyException("Simulated concurrent writer.");
        }
        public Task<StoredInboundReceipt?> ReadAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<IncomingPacs008Reference?> ReadOriginalReferencesAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class UnusedUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveAsync(CancellationToken token = default) => throw new NotSupportedException();
    }
}
