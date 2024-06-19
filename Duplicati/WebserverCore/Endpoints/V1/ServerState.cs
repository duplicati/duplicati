using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class ServerState : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/serverstate", ([FromQuery] long? lastEventId, [FromQuery] bool? longpoll, string? duration, [FromServices] IStatusService statusService)
            => Execute(lastEventId, longpoll ?? false, duration, statusService))
            .RequireAuthorization();

        group.MapPost("/serverstate/pause", ([FromServices] IStatusService statusService, [FromServices] LiveControls liveControls, [FromQuery] string? duration) => ExecutePause(statusService, liveControls, duration)).RequireAuthorization();
        group.MapPost("/serverstate/resume", ([FromServices] IStatusService statusService, [FromServices] LiveControls liveControls) => ExecuteResume(statusService, liveControls)).RequireAuthorization();
    }

    private static Dto.ServerStatusDto Execute(long? lastEventId, bool longpoll, string? duration, IStatusService statusService)
    {
        var status = statusService.GetStatus();
        if (longpoll == true)
        {
            if (lastEventId == null)
                throw new BadRequestException("When activating long poll, the request must include the last event id");

            if (duration == null)
                throw new BadRequestException("When activating long poll, the request must include the duration");

            var ts = TimeSpan.Zero;
            try { ts = Library.Utility.Timeparser.ParseTimeSpan(duration); }
            catch { throw new BadRequestException("When activating long poll, the request must include the duration and it must be a valid time span"); }

            if (ts.TotalSeconds < 10 || ts.TotalSeconds > 3600)
                throw new BadRequestException("The duration must be between 10 seconds and 1 hour");

            var id = FIXMEGlobal.StatusEventNotifyer.Wait(lastEventId.Value, (int)ts.TotalMilliseconds);
            // If the id changed, regenerate the response object
            if (id != lastEventId)
                status = statusService.GetStatus();
        }
        return status;
    }

    private static void ExecutePause(IStatusService statusService, LiveControls liveControls, string? duration)
    {
        var ts = TimeSpan.Zero;
        if (duration != null)
            try { ts = Library.Utility.Timeparser.ParseTimeSpan(duration); }
            catch { throw new BadRequestException("The duration must be a valid time span"); }

        if (ts.TotalMilliseconds > 0)
            liveControls.Pause(ts);
        else
            liveControls.Pause();
    }

    private static void ExecuteResume(IStatusService statusService, LiveControls liveControls)
    {
        liveControls.Resume();
    }
}