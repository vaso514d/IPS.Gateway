using System.Data.Common;
using System.Text.Json;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Execution;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Payments.Transport;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.IntegrationTests.Payments;

internal static class OutgoingProcessProbe
{
    internal sealed record Settings(
        string Connection,
        OutgoingTransportSettings Transport,
        string Checkpoint,
        string Signal,
        bool Initialize,
        string MessageType = PaymentMessageTypes.Pacs008);
    internal static async Task RunAsync(string file)
    {
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(file))!;
        if (!settings.Connection.Contains("IPS_Middleware_Tests_", StringComparison.Ordinal) || !settings.Connection.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Probe requires an isolated LocalDB database.");
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddPersistence(settings.Connection);
        var pause = new PauseAfterCommit(settings);
        builder.Services.AddDbContext<TransactionDbContext>(o => o.AddInterceptors(pause));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(settings.Transport);
        builder.Services.AddSingleton(new Pacs008SigningPolicy(false, false));
        builder.Services.AddSingleton<Pacs008MessageSigner>();
        builder.Services.AddOutgoingHttpClients();
        builder.Services.AddSingleton(new Pacs008Options(ownership: TimeSpan.FromSeconds(6), persistenceBudget: TimeSpan.FromMilliseconds(500)));
        builder.Services.AddSingleton(new StatusDeliveryOptions(discoveryInterval: TimeSpan.FromMilliseconds(50)));
        builder.Services.AddSingleton(new OutgoingExecutionOptions(enabled: true, httpWait: TimeSpan.FromSeconds(3.5), attemptBudget: TimeSpan.FromSeconds(4), discoveryInterval: TimeSpan.FromMilliseconds(50)));
        builder.Services.AddSingleton(new InvestigationOptions(callTimeout: TimeSpan.FromSeconds(3.5), attemptBudget: TimeSpan.FromSeconds(4),
            ownership: TimeSpan.FromSeconds(6), persistenceBudget: TimeSpan.FromMilliseconds(500), discoveryInterval: TimeSpan.FromMilliseconds(50)));
        builder.Services.AddOutgoingInvestigation();
        builder.Services.AddScoped<OutgoingTransactionIntake>();
        builder.Services.AddScoped<OutgoingTransactionWork>();
        builder.Services.AddScoped<OutgoingPaymentProcessing>();
        builder.Services.AddScoped<OutgoingStatusDelivery>();
        builder.Services.AddScoped(sp => new Pacs008Intake(sp.GetRequiredService<IOutgoingPaymentRepository>(), sp.GetRequiredService<OutgoingTransactionIntake>(),
            Pacs008Fixture.Policy, new("NBGEGE22"), sp.GetRequiredService<Pacs008Options>(), TimeProvider.System));
        builder.Services.AddScoped(sp => new Pacs009Intake(sp.GetRequiredService<IOutgoingPaymentRepository>(), sp.GetRequiredService<OutgoingTransactionIntake>(),
            Pacs008Fixture.Policy, new("NBGEGE22"), TimeProvider.System));
        builder.Services.AddScoped(sp => new Pacs004Intake(sp.GetRequiredService<IOutgoingPaymentRepository>(), sp.GetRequiredService<OutgoingTransactionIntake>(),
            Pacs008Fixture.Policy, new("NBGEGE22"), TimeProvider.System));
        builder.Services.AddScoped(sp => new Camt056Intake(sp.GetRequiredService<IOutgoingPaymentRepository>(), sp.GetRequiredService<OutgoingTransactionIntake>(),
            Pacs008Fixture.Policy, new("NBGEGE22"), TimeProvider.System));
        builder.Services.AddScoped(sp => new Camt029Intake(sp.GetRequiredService<IOutgoingPaymentRepository>(), sp.GetRequiredService<OutgoingTransactionIntake>(),
            Pacs008Fixture.Policy, new("NBGEGE22"), TimeProvider.System));
        builder.Services.AddSingleton<OutgoingRuntime>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<OutgoingRuntime>());
        using var host = builder.Build();
        if (settings.Initialize)
        {
            await using var scope = host.Services.CreateAsyncScope();
            if (settings.MessageType == PaymentMessageTypes.Pacs009)
            {
                var payment = Pacs009Fixture.Request("crash");
                await scope.ServiceProvider.GetRequiredService<Pacs009Intake>().AcceptAsync(payment, JsonSerializer.Serialize(payment), default);
            }
            else if (settings.MessageType == PaymentMessageTypes.Camt029)
            {
                var payment = Camt029Fixture.Request("crash");
                await scope.ServiceProvider.GetRequiredService<Camt029Intake>().AcceptAsync(payment, JsonSerializer.Serialize(payment), default);
            }
            else if (settings.MessageType == PaymentMessageTypes.Camt056)
            {
                var payment = Camt056Fixture.Request("crash");
                await scope.ServiceProvider.GetRequiredService<Camt056Intake>().AcceptAsync(payment, JsonSerializer.Serialize(payment), default);
            }
            else if (settings.MessageType == PaymentMessageTypes.Pacs004)
            {
                var payment = Pacs004Fixture.Request("crash");
                await scope.ServiceProvider.GetRequiredService<Pacs004Intake>().AcceptAsync(payment, JsonSerializer.Serialize(payment), default);
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                var request = Pacs008Fixture.Request() with
                {
                    ClientReference = "crash",
                    CreationDateTime = now.AddMilliseconds(-500),
                    AcceptanceDateTime = now
                };
                await scope.ServiceProvider.GetRequiredService<Pacs008Intake>().AcceptAsync(request, JsonSerializer.Serialize(request), default);
            }
        }

        await host.RunAsync();
    }

    private sealed class PauseAfterCommit(Settings settings) : DbTransactionInterceptor
    {
        private int paused;
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            var payment = db.ChangeTracker.Entries<OutgoingPayment>().SingleOrDefault();
            var messages = db.ChangeTracker.Entries<OutgoingMessageRow>().Select(e => e.Entity).ToArray();
            var reached = settings.Checkpoint switch
            {
                "intake" => payment?.Entity.CurrentStatus == TransactionStatus.Received,
                "unsigned" => db.ChangeTracker.Entries<OutgoingPaymentMetadata>().Any(p => p.Entity.UnsignedXml is not null),
                "ready" => messages.Any(m => m.Direction == OutgoingMessageDirection.Outbound && m.Status == MessageJournalStatus.ReadyToSend),
                "marker" => messages.Any(m => m.Direction == OutgoingMessageDirection.Outbound && m.Status == MessageJournalStatus.SendStarted),
                "response" => messages.Any(m => m.Direction == OutgoingMessageDirection.Response && m.Status == MessageJournalStatus.Received),
                "outcome" => payment?.Entity.CurrentStatus == TransactionStatus.Accepted,
                "lost-reply" => payment?.Entity.CurrentStatus == TransactionStatus.Uncertain,
                "resend-ready" => messages.Any(m => m.ResendId is not null && m.Status == MessageJournalStatus.ReadyToSend),
                "resend-marker" => messages.Any(m => m.ResendId is not null && m.Status == MessageJournalStatus.SendStarted),
                "resend-response" => messages.Any(m => m.ResendId is not null && m.Status == MessageJournalStatus.Received),
                _ => false
            };
            if (!reached || Interlocked.Exchange(ref paused, 1) != 0)
            {
                return;
            }

            await File.WriteAllTextAsync(settings.Signal, settings.Checkpoint, CancellationToken.None);
            await Task.Delay(Timeout.Infinite, CancellationToken.None);
        }
    }
}
