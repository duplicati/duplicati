using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class ServerState : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/serverstate",
                ([FromQuery] long? lastEventId, [FromQuery] bool? longpoll, string? duration,
                        [FromServices] IStatusService statusService, [FromServices] EventPollNotify statusEventNotifier)
                    => Execute(lastEventId, longpoll ?? false, duration, statusService, statusEventNotifier))
            .RequireAuthorization();

        group.MapPost("/serverstate/pause",
            ([FromServices] IStatusService statusService, [FromServices] LiveControls liveControls,
                [FromQuery] string? duration, [FromQuery] bool? pauseTransfers) => ExecutePause(liveControls, duration, pauseTransfers)).RequireAuthorization();

        group.MapPost("/serverstate/resume",
            ([FromServices] IStatusService statusService, [FromServices] LiveControls liveControls) =>
                ExecuteResume(liveControls)).RequireAuthorization();
    }

    private static ServerStatusDto Execute(long? lastEventId, bool longpoll, string? duration,
        IStatusService statusService, EventPollNotify statusEventNotifier)
    {
        var status = statusService.GetStatus();
        if (longpoll)
        {
            if (lastEventId == null)
                throw new BadRequestException("When activating long poll, the request must include the last event id");

            if (duration == null)
                throw new BadRequestException("When activating long poll, the request must include the duration");

            TimeSpan ts;
            try
            {
                ts = Library.Utility.Timeparser.ParseTimeSpan(duration);
            }
            catch
            {
                throw new BadRequestException(
                    "When activating long poll, the request must include the duration and it must be a valid time span");
            }

            if (ts.TotalSeconds is < 10 or > 3600)
                throw new BadRequestException("The duration must be between 10 seconds and 1 hour");

            var id = statusEventNotifier.Wait(lastEventId.Value, (int)ts.TotalMilliseconds);
            // If the id changed, regenerate the response object
            if (id != lastEventId)
                status = statusService.GetStatus();
        }

        return status;
    }

    private static void ExecutePause(LiveControls liveControls, string? duration, bool? pauseTransfer)
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
            liveControls.Pause(ts, pauseTransfer ?? false);
        else
            liveControls.Pause(pauseTransfer ?? false);
    }

    private static void ExecuteResume(LiveControls liveControls)
    {
        liveControls.Resume();
    }
}