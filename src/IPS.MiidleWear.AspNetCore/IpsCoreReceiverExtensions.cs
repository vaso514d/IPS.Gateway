using IPS.MiidleWear.Contracts.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.MiidleWear.AspNetCore;

/// <summary>Registration and endpoint mapping of a core system's <see cref="IClientPaymentReceiver"/>.</summary>
public static class IpsCoreReceiverExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TReceiver"/> as the scoped <see cref="IClientPaymentReceiver"/> and the scoped
    /// <see cref="IIpsReceiveContext"/> it can inject.
    /// </summary>
    public static IServiceCollection AddIpsCoreReceiver<TReceiver>(this IServiceCollection services)
        where TReceiver : class, IClientPaymentReceiver
    {
        services.AddScoped<IClientPaymentReceiver, TReceiver>();
        services.AddScoped<IpsReceiveContext>();
        services.AddScoped<IIpsReceiveContext>(provider => provider.GetRequiredService<IpsReceiveContext>());
        return services;
    }

    /// <summary>
    /// Maps every <see cref="IClientPaymentReceiver"/> operation at the route its <c>RestEndpoint</c> attribute declares:
    /// the seven receive calls, the status callback and the payment-status query. Requests are read and answers written
    /// with <c>IpsJson.SerializerOptions</c>, whatever JSON options the application uses. A receive answer is 200, the
    /// status callback 204, a <c>null</c> payment-status result 404, an unreadable body 400 and an exception from the
    /// receiver 500, which the gateway treats as no answer and asks again.
    /// <para>
    /// Returns the group of these endpoints, so the application can add its own conventions, for example authorization.
    /// Throws at startup when <see cref="AddIpsCoreReceiver{TReceiver}"/> was not called or an operation is not mapped.
    /// </para>
    /// </summary>
    public static RouteGroupBuilder MapIpsCoreReceiver(this IEndpointRouteBuilder endpoints)
    {
        RequireReceiver(endpoints.ServiceProvider);
        // The endpoints belong to the receiver document only, so the application's own documents do not list them.
        var group = endpoints.MapGroup(string.Empty).WithGroupName(CoreReceiverOpenApi.DocumentName);
        CoreReceiverEndpoints.Map(group);
        return group;
    }

    private static void RequireReceiver(IServiceProvider services)
    {
        var registrations = services.GetService<IServiceProviderIsService>();
        if (registrations is not null && !registrations.IsService(typeof(IpsReceiveContext)))
        {
            throw new InvalidOperationException(
                $"Call services.{nameof(AddIpsCoreReceiver)}<TReceiver>() before app.{nameof(MapIpsCoreReceiver)}().");
        }
    }
}
