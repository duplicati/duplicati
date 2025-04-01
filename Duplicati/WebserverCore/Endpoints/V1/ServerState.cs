// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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