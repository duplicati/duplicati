using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Antiforgery;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Antiforgery : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("antiforgery/token", (IAntiforgery forgeryService, HttpContext context) =>
        {
            var tokens = forgeryService.GetAndStoreTokens(context);
            var xsrfToken = tokens.RequestToken!;
            return TypedResults.Content(xsrfToken, "text/plain");
        });
    }
}
