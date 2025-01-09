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
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class ServerStatus
{
    public static Command Create() =>
        new Command("status", "Gets the server status")
        .WithHandler(CommandHandler.Create<Settings>(async (settings) =>
        {
            var state = await (await settings.GetConnection()).GetServerState();
            Console.WriteLine($"Server state: {state.ProgramState}");
            if (state.ActiveTask != null)
                Console.WriteLine($"Active task: [Task {state.ActiveTask.Item1}]: BackupId = {state.ActiveTask.Item2}");
            else
                Console.WriteLine("Active task: None");

            if (state.SchedulerQueueIds.Any())
            {
                Console.WriteLine("Scheduled tasks:");
                foreach (var id in state.SchedulerQueueIds)
                    Console.WriteLine($"  [Task {id.Item1}]: BackupId = {id.Item2}");
            }
            else
                Console.WriteLine("Scheduler tasks: Empty");
        }));

}
