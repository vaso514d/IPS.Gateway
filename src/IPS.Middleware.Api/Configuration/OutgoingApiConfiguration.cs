using IPS.Middleware.Api.Binding;
using IPS.Middleware.Api.Payments;
using IPS.Middleware.Application.Payments.Execution;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace IPS.Middleware.Api.Configuration;

internal static class OutgoingApiConfiguration
{
    internal static IServiceCollection AddOutgoingApi(this IServiceCollection services)
    {
        services.AddControllers(options =>
        {
            options.InputFormatters.RemoveType<SystemTextJsonInputFormatter>();
            options.InputFormatters.Add(new HttpJsonInputFormatter());
        }).ConfigureApiBehaviorOptions(options =>
        {
            options.SuppressMapClientErrors = true;
            options.InvalidModelStateResponseFactory = context => new StatusCodeResult(
                context.HttpContext.Response.StatusCode >= StatusCodes.Status400BadRequest
                    ? context.HttpContext.Response.StatusCode
                    : StatusCodes.Status400BadRequest);
        });
        services.AddOptions<MvcOptions>().Configure<OutgoingExecutionOptions>((mvc, execution) =>
            mvc.Conventions.Add(new OutgoingControllerConvention(execution.Enabled)));
        return services;
    }

    private sealed class OutgoingControllerConvention(bool enabled) : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            if (!enabled)
            {
                var outgoing = application.Controllers.Single(controller => controller.ControllerType == typeof(OutgoingPaymentsController));
                application.Controllers.Remove(outgoing);
            }
        }
    }
}
