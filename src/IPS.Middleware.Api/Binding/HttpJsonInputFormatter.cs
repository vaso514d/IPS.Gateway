using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace IPS.Middleware.Api.Binding;

// Keep the existing HTTP JSON reader, including its media types, encodings and configured serializer options.
internal sealed class HttpJsonInputFormatter : InputFormatter
{
    public HttpJsonInputFormatter()
    {
        SupportedMediaTypes.Add("application/json");
    }

    public override bool CanRead(InputFormatterContext context) =>
        context.HttpContext.Request.HasJsonContentType() ||
        (context.HttpContext.Request.ContentType is null &&
         context.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true);

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        try
        {
            var request = context.HttpContext.Request;
            var model = await request.ReadFromJsonAsync(context.ModelType, context.HttpContext.RequestAborted);
            return model is null ? InputFormatterResult.NoValue() : InputFormatterResult.Success(model);
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            context.HttpContext.Response.StatusCode = error is BadHttpRequestException requestError
                ? requestError.StatusCode
                : StatusCodes.Status400BadRequest;
            context.ModelState.TryAddModelError(context.ModelName, error, context.Metadata);
            return InputFormatterResult.Failure();
        }
    }
}
