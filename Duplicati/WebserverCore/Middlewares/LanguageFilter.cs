using Duplicati.Library.Localization;

namespace Duplicati.WebserverCore.Middlewares;

public class LanguageFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var ci = (context.HttpContext.Request != null && context.HttpContext.Request.Headers.TryGetValue("X-UI-Language", out var locale) && !string.IsNullOrWhiteSpace(locale))
            ? LocalizationService.ParseCulture(locale)
            : null;

        using (ci == null ? null : LocalizationService.TemporaryContext(ci))
            return await next(context);

    }
}

