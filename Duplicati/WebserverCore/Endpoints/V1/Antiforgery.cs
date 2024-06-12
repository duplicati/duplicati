using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Antiforgery : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("antiforgery/token", ([FromServices] IAntiforgery forgeryService, [FromServices] IHttpContextAccessor context) =>
        {
            var tokens = forgeryService.GetAndStoreTokens(context.HttpContext!);
            var xsrfToken = tokens.RequestToken!;
            return TypedResults.Content(xsrfToken, "text/plain");
        });
    }
}
