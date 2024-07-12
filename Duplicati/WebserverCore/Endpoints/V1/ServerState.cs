using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class ServerState : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/serverstate/pause",
            ([FromServices] IStatusService statusService, [FromServices] LiveControls liveControls,
                [FromQuery] string? duration) => ExecutePause(liveControls, duration)).RequireAuthorization();
        group.MapPost("/serverstate/resume",
            ([FromServices] IStatusService statusService, [FromServices] LiveControls liveControls) =>
                ExecuteResume(liveControls)).RequireAuthorization();
    }

    private static void ExecutePause(LiveControls liveControls, string? duration)
    {
        var ts = TimeSpan.Zero;
        if (duration != null)
            try
            {
                ts = Library.Utility.Timeparser.ParseTimeSpan(duration);
            }
            catch
            {
                throw new BadRequestException("The duration must be a valid time span");
            }

        if (ts.TotalMilliseconds > 0)
            liveControls.Pause(ts);
        else
            liveControls.Pause();
    }

    private static void ExecuteResume(LiveControls liveControls)
    {
        liveControls.Resume();
    }
}