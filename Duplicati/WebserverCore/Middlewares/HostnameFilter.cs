using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Middlewares;

public class HostnameFilter(IHostnameValidator hostnameValidator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var hostname = context.HttpContext.Request.Host.Host;
        if (!hostnameValidator.IsValidHostname(hostname))
        {
            context.HttpContext.Response.StatusCode = 403;
            context.HttpContext.Response.Headers.Append("Content-Type", "text/plain");
            await context.HttpContext.Response.WriteAsync("Invalid hostname");
            return null;
        }
        return await next(context);
    }
}
