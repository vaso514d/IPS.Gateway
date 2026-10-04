using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TransactionDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IOutgoingPaymentRepository, OutgoingPaymentRepository>();
        services.AddScoped<ITransactionWorkRepository, TransactionWorkRepository>();
        services.AddScoped<IPaymentPreparationRepository, PaymentPreparationRepository>();
        services.AddScoped<IPaymentSubmissionRepository, PaymentSubmissionRepository>();
        services.AddScoped<IInboundReceiptRepository, InboundReceiptRepository>();
        services.AddScoped<IInboundWorkRepository, InboundWorkRepository>();
        services.AddScoped<IIncomingPaymentRepository, IncomingPaymentRepository>();
        services.AddScoped<IIncomingPaymentWorkRepository, IncomingPaymentWorkRepository>();
        services.AddScoped<IIncomingProcessingRepository, IncomingProcessingRepository>();
        services.AddScoped<IIncomingReconciliationRepository, IncomingReconciliationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork.UnitOfWork>();
        return services;
    }
}
