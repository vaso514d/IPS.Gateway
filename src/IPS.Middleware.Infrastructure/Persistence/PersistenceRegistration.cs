using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
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
        services.AddScoped<IUnitOfWork, UnitOfWork.UnitOfWork>();
        return services;
    }
}
