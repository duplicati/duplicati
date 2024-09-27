
namespace Duplicati.WebserverCore.Middlewares;

public class PreSharedKeyFilter : IEndpointFilter
{
    public const string HeaderName = "X-Duplicati-PreSharedKey";
    public static string? PreSharedKey = null;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (string.IsNullOrEmpty(PreSharedKey))
        {
            context.HttpContext.Response.StatusCode = 500;
            context.HttpContext.Response.Headers.Append("Content-Type", "text/plain");
            await context.HttpContext.Response.WriteAsync("PreSharedKey not set");
            return null;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var headerValue) || headerValue != PreSharedKey)
        {
            context.HttpContext.Response.StatusCode = 403;
            context.HttpContext.Response.Headers.Append("Content-Type", "text/plain");
            await context.HttpContext.Response.WriteAsync("Invalid PreSharedKey");
            return null;
        }

        return await next(context);
    }
}
