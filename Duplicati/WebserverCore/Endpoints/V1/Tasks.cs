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
