
namespace Duplicati.WebserverCore
{
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Server.WebServer.AuthenticationHandler _authHandler = new();

        public AuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var info = new Server.WebServer.RESTMethods.RequestInfo(new LegacyHttpRequestShim(context.Request), new LegacyHttpResponseShim(context.Response), new LegacyHttpSessionShim());

            if (!_authHandler.Process(info.Request, info.Response, info.Session))
            {
                // Call the next delegate/middleware in the pipeline.
                await _next(context);
            }
            else
            {
                info.Response.Send();
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