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
using Duplicati.Server.Database;
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

        group.MapPost("/commandline", ([FromServices] Connection connection, [FromServices] ICommandlineRunService commandlineRunService, [FromBody] string[] input)
            => ExecuteRunCommand(connection, commandlineRunService, input))
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

    private static Dto.CommandlineTaskStartedDto ExecuteRunCommand(Connection connection, ICommandlineRunService commandlineRunService, string[] arguments)
    {
        if (arguments.Length == 0)
            throw new BadRequestException("No arguments provided");
        if (CommandLine.Program.SupportedCommands.Contains(arguments[0]) == false)
            throw new BadRequestException("Command not supported");

        // Parse arguments into args + options, so we can replace any password placeholders
        var args = new List<string>();
        var options = new List<KeyValuePair<string, string?>>();
        var inArgParse = false;

        foreach (var n in arguments)
        {
            if (n.StartsWith("--", StringComparison.Ordinal))
            {
                var eq = n.IndexOf('=');
                if (eq > 2)
                {
                    var key = n.Substring(2, eq - 2);
                    var value = n.Substring(eq + 1);
                    if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                        value = value.Substring(1, value.Length - 2);
                    options.Add(new KeyValuePair<string, string?>(key, value));
                    inArgParse = false;
                }
                else
                {
                    var key = n.Substring(2);
                    options.Add(new KeyValuePair<string, string?>(key, null));
                    inArgParse = true;
                }
            }
            // Support "--key value" format
            else if (inArgParse)
            {
                var last = options[^1];
                options[^1] = new KeyValuePair<string, string?>(last.Key, n);
                inArgParse = false;
            }
            else
            {
                args.Add(n);
            }
        }

        var backupId = options.FirstOrDefault(x => x.Key.Equals("backup-id", StringComparison.OrdinalIgnoreCase)).Value;
        if (backupId != null)
        {
            var backup = connection.GetBackup(backupId);
            if (backup != null)
            {
                if (args.Count > 1)
                    args[1] = QuerystringMasking.Unmask(args[1], backup.TargetURL);

                var settings = backup.Settings.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < options.Count; i++)
                {
                    var opt = options[i];
                    if (settings.TryGetValue(opt.Key, out var val) && Connection.IsPasswordPlaceholder(opt.Value))
                        options[i] = new KeyValuePair<string, string?>(opt.Key, val);
                }
            }
        }

        arguments = args.Concat(options.Select(x => x.Value == null ? $"--{x.Key}" : $"--{x.Key}={x.Value}")).ToArray();

        return new Dto.CommandlineTaskStartedDto("OK", commandlineRunService.StartTask(arguments));
    }

}
