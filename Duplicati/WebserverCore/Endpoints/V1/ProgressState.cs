using Duplicati.Library.RestAPI;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class ProgressState : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/progressstate", Execute)
        .RequireAuthorization();
    }

    private static Server.Serialization.Interface.IProgressEventData Execute()
    {
        if (FIXMEGlobal.GenerateProgressState == null)
            throw new NotFoundException("No active backup");

        return FIXMEGlobal.GenerateProgressState();
    }
}
