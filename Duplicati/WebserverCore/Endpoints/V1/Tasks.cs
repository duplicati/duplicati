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
using Duplicati.Library.RestAPI;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Tasks : IEndpointV1
{
    private enum TaskStopState
    {
        Stop,
        Abort
    }
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/tasks", Execute).RequireAuthorization();
        group.MapGet("/task/{taskid}", ([FromRoute] long taskId) => ExecuteGet(taskId)).RequireAuthorization();
        group.MapPost("/task/{taskid}/stop", ([FromRoute] long taskId) => ExecutePost(taskId, TaskStopState.Stop)).RequireAuthorization();
        group.MapPost("/task/{taskid}/abort", ([FromRoute] long taskId) => ExecutePost(taskId, TaskStopState.Abort)).RequireAuthorization();

    }

    private static IEnumerable<Server.Runner.IRunnerData> Execute()
    {
        var cur = FIXMEGlobal.WorkThread.CurrentTask;
        var n = FIXMEGlobal.WorkThread.CurrentTasks;

        if (cur != null)
            n.Insert(0, cur);

        return n;
    }

    private static Dto.GetTaskStateDto ExecuteGet(long taskid)
    {
        var task = FIXMEGlobal.WorkThread.CurrentTask;
        var tasks = FIXMEGlobal.WorkThread.CurrentTasks;

        if (task != null && task.TaskID == taskid)
            return new Dto.GetTaskStateDto("Running", taskid);

        if (tasks.FirstOrDefault(x => x.TaskID == taskid) == null)
        {
            KeyValuePair<long, Exception>[] matches;
            lock (FIXMEGlobal.MainLock)
                matches = FIXMEGlobal.TaskResultCache.Where(x => x.Key == taskid).ToArray();

            if (matches.Length == 0)
                throw new NotFoundException("No such task found");


            return new Dto.GetTaskStateDto(
                Status: matches[0].Value == null ? "Completed" : "Failed",
                ID: taskid,
                ErrorMessage: matches[0].Value == null ? null : matches[0].Value.Message,
                Exception: matches[0].Value == null ? null : matches[0].Value.ToString()
            );
        }

        return new Dto.GetTaskStateDto("Waiting", taskid);
    }

    private static void ExecutePost(long taskid, TaskStopState stopState)
    {
        var task = FIXMEGlobal.WorkThread.CurrentTask;
        var tasks = FIXMEGlobal.WorkThread.CurrentTasks;

        if (task != null)
            tasks.Insert(0, task);

        task = tasks.FirstOrDefault(x => x.TaskID == taskid);
        if (task == null)
            throw new NotFoundException("No such task found");

        switch (stopState)
        {
            case TaskStopState.Stop:
                task.Stop();
                break;
            case TaskStopState.Abort:
                task.Abort();
                break;

            default:
                throw new NotImplementedException();
        }
    }
}
