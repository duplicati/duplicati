using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Commandline : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/commandline", ExecuteGetCommands);

        group.MapGet("/commandline/{runid}", ([FromServices] ICommandlineRunService commandlineRunService, [FromRoute] string runid, int? offset, int? pagesize)
            => ExecuteGetLogCommand(commandlineRunService, runid, offset ?? 0, pagesize ?? 100))
            .RequireAuthorization();

        group.MapPost("/commandline/{runid}/abort", ([FromServices] ICommandlineRunService commandlineRunService, [FromRoute] string runid)
            => ExecuteAbortCommand(commandlineRunService, runid))
            .RequireAuthorization();

        group.MapPost("/commandline", ([FromServices] ICommandlineRunService commandlineRunService, [FromBody] string[] input)
            => ExecuteRunCommand(commandlineRunService, input))
            .RequireAuthorization();
    }

    private static IEnumerable<string> ExecuteGetCommands()
        => CommandLine.Program.SupportedCommands;

    private static Dto.CommandLineLogOutputDto ExecuteGetLogCommand(ICommandlineRunService commandlineRunService, string runid, int offset, int pagesize)
    {
        var activeRun = commandlineRunService.GetActiveRun(runid)
            ?? throw new NotFoundException("Command not found");

        pagesize = Math.Max(10, Math.Min(500, pagesize));
        offset = Math.Max(0, offset);
        var items = new List<string>();

        int count;
        bool started;
        bool finished;

        lock (activeRun.Lock)
        {
            var log = activeRun.GetLog();
            count = log.Count();
            offset = Math.Min(count, offset);
            items.AddRange(log.Skip(offset).Take(pagesize));
            finished = activeRun.Finished;
            started = activeRun.Started;
        }

        return new Dto.CommandLineLogOutputDto(
            Pagesize: pagesize,
            Offset: offset,
            Count: count,
            Items: items,
            Finished: finished,
            Started: started
        );
    }

    private static void ExecuteAbortCommand(ICommandlineRunService commandlineRunService, string runid)
    {
        var activeRun = commandlineRunService.GetActiveRun(runid)
            ?? throw new NotFoundException("Command not found");

        activeRun.Abort();
    }

    private static Dto.CommandlineTaskStartedDto ExecuteRunCommand(ICommandlineRunService commandlineRunService, string[] arguments)
    {
        if (arguments.Length == 0)
            throw new BadRequestException("No arguments provided");
        if (CommandLine.Program.SupportedCommands.Contains(arguments[0]) == false)
            throw new BadRequestException("Command not supported");
        return new Dto.CommandlineTaskStartedDto("OK", commandlineRunService.StartTask(arguments));
    }

}
