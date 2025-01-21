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
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1.Backup;

public class BackupPost : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/backup/{id}/deletedb", ([FromServices] Connection connection, [FromRoute] string id)
            => ExecuteDeleteDb(GetBackup(connection, id)))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/movedb", ([FromServices] Connection connection, [FromRoute] string id, [FromBody] Dto.UpdateDbPathInputDto input)
            => UpdateDatabasePath(connection, GetBackup(connection, id), input.path, true))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/updatedb", ([FromServices] Connection connection, [FromRoute] string id, [FromBody] Dto.UpdateDbPathInputDto input)
            => UpdateDatabasePath(connection, GetBackup(connection, id), input.path, false))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/restore", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id, [FromBody] Dto.RestoreInputDto input)
            => ExecuteRestore(GetBackup(connection, id), workerThreadsManager, input))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/createreport", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id)
            => ExecuteCreateReport(GetBackup(connection, id), workerThreadsManager))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/repair", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id, Dto.RepairInputDto? input)
            => ExecuteRepair(GetBackup(connection, id), workerThreadsManager, input))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/repairupdate", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id, Dto.RepairInputDto? input)
            => ExecuteRepairUpdate(GetBackup(connection, id), workerThreadsManager, input))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/vacuum", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id)
            => ExecuteVacuum(GetBackup(connection, id), workerThreadsManager))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/verify", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id)
            => ExecuteVerify(GetBackup(connection, id), workerThreadsManager))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/compact", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id)
            => ExecuteCompact(GetBackup(connection, id), workerThreadsManager))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/start", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id)
            => ExecuteRunBackup(GetBackup(connection, id), workerThreadsManager))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/run", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id)
            => ExecuteRunBackup(GetBackup(connection, id), workerThreadsManager))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/report-remote-size", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id)
            => ExecuteReportRemoteSize(GetBackup(connection, id), workerThreadsManager))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/copytotemp", ([FromServices] Connection connection, [FromRoute] string id)
            => ExecuteCopyToTemp(GetBackup(connection, id), connection))
            .RequireAuthorization();
    }

    private static IBackup GetBackup(Connection connection, string id)
        => connection.GetBackup(id) ?? throw new NotFoundException("Backup not found");

    private static void ExecuteDeleteDb(IBackup backup)
        => File.Delete(backup.DBPath);

    private static void UpdateDatabasePath(Connection connection, IBackup backup, string targetpath, bool move)
    {
        if (string.IsNullOrWhiteSpace(targetpath))
            throw new BadRequestException("No target path supplied");
        if (!Path.IsPathRooted(targetpath))
            throw new BadRequestException("Target path is relative, please supply a fully qualified path");

        if (move && (File.Exists(targetpath) || Directory.Exists(targetpath)))
            throw new ConflictException("A file already exists at the new location");

        if (move)
            File.Move(backup.DBPath, targetpath);

        connection.UpdateBackupDBPath(backup, targetpath);
    }



    private static Dto.TaskStartedDto ExecuteRestore(IBackup backup, IWorkerThreadsManager workerThreadsManager, Dto.RestoreInputDto input)
        => new Dto.TaskStartedDto("OK", workerThreadsManager.AddTask(Runner.CreateRestoreTask(
            backup,
            input.paths ?? [],
            Library.Utility.Timeparser.ParseTimeInterval(input.time, DateTime.Now),
            input.restore_path,
            input.overwrite ?? false,
            input.permissions ?? false,
            input.skip_metadata ?? false,
            string.IsNullOrWhiteSpace(input.passphrase) ? null : input.passphrase)));

    private static Dto.TaskStartedDto ExecuteCreateReport(IBackup backup, IWorkerThreadsManager workerThreadsManager)
        => new Dto.TaskStartedDto("OK", workerThreadsManager.AddTask(Runner.CreateTask(DuplicatiOperation.CreateReport, backup)));

    private static Dto.TaskStartedDto ExecuteReportRemoteSize(IBackup backup, IWorkerThreadsManager workerThreadsManager)
        => new Dto.TaskStartedDto("OK", workerThreadsManager.AddTask(Runner.CreateTask(DuplicatiOperation.ListRemote, backup)));

    private static Dto.TaskStartedDto ExecuteRepair(IBackup backup, IWorkerThreadsManager workerThreadsManager, Dto.RepairInputDto? input)
        => DoRepair(backup, false, workerThreadsManager, input);

    private static Dto.TaskStartedDto ExecuteRepairUpdate(IBackup backup, IWorkerThreadsManager workerThreadsManager, Dto.RepairInputDto? input)
        => DoRepair(backup, true, workerThreadsManager, input);

    private static Dto.TaskStartedDto ExecuteVacuum(IBackup backup, IWorkerThreadsManager workerThreadsManager)
        => new Dto.TaskStartedDto("OK", workerThreadsManager.AddTask(Runner.CreateTask(DuplicatiOperation.Vacuum, backup)));

    private static Dto.TaskStartedDto ExecuteVerify(IBackup backup, IWorkerThreadsManager workerThreadsManager)
        => new Dto.TaskStartedDto("OK", workerThreadsManager.AddTask(Runner.CreateTask(DuplicatiOperation.Verify, backup)));

    private static Dto.TaskStartedDto ExecuteCompact(IBackup backup, IWorkerThreadsManager workerThreadsManager)
        => new Dto.TaskStartedDto("OK", workerThreadsManager.AddTask(Runner.CreateTask(DuplicatiOperation.Compact, backup)));

    private static Dto.TaskStartedDto DoRepair(IBackup backup, bool repairUpdate, IWorkerThreadsManager workerThreadsManager, Dto.RepairInputDto? input)
    {
        // These are all props on the input object
        var extra = new Dictionary<string, string>();
        if (input != null)
        {
            if (input.only_paths.HasValue)
                extra["repair-only-paths"] = input.only_paths.Value.ToString();
            if (!string.IsNullOrWhiteSpace(input.time))
                extra["time"] = input.time;
            if (!string.IsNullOrWhiteSpace(input.version))
                extra["version"] = input.version;
        }

        var filters = input?.paths ?? [];

        return new Dto.TaskStartedDto("OK", workerThreadsManager.AddTask(Runner.CreateTask(repairUpdate ? DuplicatiOperation.RepairUpdate : DuplicatiOperation.Repair, backup, extra, filters)));
    }

    private static Dto.TaskStartedDto ExecuteRunBackup(IBackup backup, IWorkerThreadsManager workerThreadsManager)
    {
        var t = workerThreadsManager.WorkerThread?.CurrentTask;
        var bt = t?.Backup;

        // Already running
        if (bt != null && backup.ID == bt.ID)
            return new Dto.TaskStartedDto("OK", t!.TaskID);

        t = workerThreadsManager.WorkerThread?.CurrentTasks.FirstOrDefault(x => x?.Backup != null && x.Backup.ID == backup.ID);
        if (t != null)
            return new Dto.TaskStartedDto("OK", t.TaskID);

        return new Dto.TaskStartedDto("OK", workerThreadsManager.AddTask(Runner.CreateTask(DuplicatiOperation.Backup, backup), true));
    }

    private class WrappedBackup : Server.Database.Backup
    {
        public string? DBPathSetter
        {
            get => DBPath;
            set => SetDBPath(value);
        }
    }

    private static Dto.CreateBackupDto ExecuteCopyToTemp(IBackup backup, Connection connection)
    {
        var ipx = Serializer.Deserialize<WrappedBackup>(new StringReader(Newtonsoft.Json.JsonConvert.SerializeObject(backup)));

        using (var tf = new Library.Utility.TempFile())
            ipx.DBPathSetter = tf;
        ipx.ID = null;

        connection.RegisterTemporaryBackup(ipx);
        return new Dto.CreateBackupDto(ipx.ID, ipx.IsTemporary);
    }
}
