using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class ServerStateIv1Endpoint : IV1Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/serverstate", (IStatusService statusService) => statusService.GetStatus());
    }
}