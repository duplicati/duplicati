using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Middlewares;

public class HostnameFilter(IHostnameValidator hostnameValidator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var hostname = context.HttpContext.Request.Host.Host;
        if (!hostnameValidator.IsValidHostname(hostname))
            throw new InvalidHostnameException(hostname);

        return await next(context);
    }
}
