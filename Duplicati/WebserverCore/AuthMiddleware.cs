
namespace Duplicati.WebserverCore
{
    public class AuthMiddleware(RequestDelegate next)
    {
        private readonly Server.WebServer.AuthenticationHandler _authHandler = new();

        public async Task InvokeAsync(HttpContext context)
        {
            var info = new Server.WebServer.RESTMethods.RequestInfo(new LegacyHttpRequestShim(context.Request), new LegacyHttpResponseShim(context.Response), new LegacyHttpSessionShim());

            if (!_authHandler.Process(info.Request, info.Response, info.Session))
            {
                // Call the next delegate/middleware in the pipeline.
                await next(context);
            }
            else
            {
                if (info.Response is LegacyHttpResponseShim legacy)
                {
                    await legacy.SendAsync();
                }
            }
        }
    }

    public static class AuthMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuthMiddleware(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuthMiddleware>();
        }
    }
}