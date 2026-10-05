using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace IPS.Middleware.Api.Binding;

// Preserve minimal endpoint binding: repeated query values form one comma-separated string.
public sealed class QueryStringValueBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var query = bindingContext.HttpContext.Request.Query;
        var value = query.TryGetValue(bindingContext.ModelName, out var values) ? values.ToString() : null;
        bindingContext.Result = ModelBindingResult.Success(value);
        return Task.CompletedTask;
    }
}
