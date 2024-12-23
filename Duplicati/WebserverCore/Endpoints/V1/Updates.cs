using Duplicati.Library.RestAPI;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Updates : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/updates/check", Execute).RequireAuthorization();
    }

    private static void Execute()
        => FIXMEGlobal.UpdatePoller.CheckNow();
}