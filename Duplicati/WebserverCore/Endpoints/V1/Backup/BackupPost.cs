// Copyright (C) 2026, The Duplicati Team
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
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serializable;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Endpoints.Shared;
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

        group.MapPost("/backup/{id}/restore", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromServices] IApplicationSettings applicationSettings, [FromRoute] string id, [FromBody] Dto.RestoreInputDto input, CancellationToken cancellationToken)
            => ExecuteRestoreAsync(connection, applicationSettings, id, queueRunnerService, input, cancellationToken))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/createreport", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id)
            => ExecuteCreateReport(GetBackup(connection, id), queueRunnerService))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/repair", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id, Dto.RepairInputDto? input)
            => ExecuteRepair(connection, GetBackup(connection, id), queueRunnerService, input))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/repairupdate", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id, Dto.RepairInputDto? input)
            => ExecuteRepairUpdate(connection, GetBackup(connection, id), queueRunnerService, input))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/vacuum", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id)
            => ExecuteVacuum(GetBackup(connection, id), queueRunnerService))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/verify", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id)
            => ExecuteVerify(GetBackup(connection, id), queueRunnerService))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/compact", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id)
            => ExecuteCompact(GetBackup(connection, id), queueRunnerService))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/start", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id, [FromQuery] bool? skipQueue)
            => ExecuteRunBackup(GetBackup(connection, id), skipQueue ?? false, queueRunnerService))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/run", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id, [FromQuery] bool? skipQueue)
            => ExecuteRunBackup(GetBackup(connection, id), skipQueue ?? false, queueRunnerService))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/report-remote-size", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id)
            => ExecuteReportRemoteSize(GetBackup(connection, id), queueRunnerService))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/copytotemp", ([FromServices] Connection connection, [FromRoute] string id)
            => ExecuteCopyToTemp(GetBackup(connection, id), connection))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/restore-task-config", ([FromServices] Connection connection, [FromServices] IQueueRunnerService queueRunnerService, [FromRoute] string id)
            => ExecuteRestoreTaskConfigAsync(connection, queueRunnerService, GetBackup(connection, id)))
            .RequireAuthorization();

        group.MapPost("/backup/{id}/importfromtemp", ([FromServices] Connection connection, [FromServices] IBackupListService backupListService, [FromBody] ImportBackupFromTempDto input)
            => ExecuteImportFromTemp(input, connection, backupListService))
            .RequireAuthorization();
    }

    private static IBackup GetBackup(Connection connection, string id)
        => connection.GetBackup(id) ?? throw new NotFoundException("Backup not found");

    private static void ExecuteDeleteDb(IBackup backup)
        // Delete the effective database (honoring a "--dbpath" advanced option) so the database the
        // backup actually uses is removed, not a stale DBPath that may not exist (see issue #1698).
        => File.Delete(Runner.GetEffectiveDBPath(backup));

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



    private static async Task<Dto.TaskStartedDto> ExecuteRestoreAsync(Connection connection, IApplicationSettings applicationSettings, string id, IQueueRunnerService queueRunnerService, Dto.RestoreInputDto input, CancellationToken cancellationToken)
    {
        var backup = GetBackup(connection, id);
        var restorepath = input.restore_path;
        if (restorepath != null && restorepath.StartsWith("@"))
        {
            var res = await SharedRemoteOperation.ExpandUrlAsync(connection, applicationSettings, restorepath.Substring(1), id, input.connection_string_id ?? -1, input.source_prefix, cancellationToken);
            if (!string.IsNullOrWhiteSpace(res.Url))
                restorepath = "@" + res.Url;
        }

        return new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreateRestoreTask(
            backup,
            input.paths ?? [],
            Library.Utility.Timeparser.ParseTimeInterval(input.time, DateTime.Now),
            restorepath,
            input.overwrite ?? false,
            input.permissions ?? false,
            input.skip_metadata ?? false,
            string.IsNullOrWhiteSpace(input.passphrase) ? null : input.passphrase)));
    }

    private static Dto.TaskStartedDto ExecuteCreateReport(IBackup backup, IQueueRunnerService queueRunnerService)
        => new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreateTask(DuplicatiOperation.CreateReport, backup)));

    private static Dto.TaskStartedDto ExecuteReportRemoteSize(IBackup backup, IQueueRunnerService queueRunnerService)
        => new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreateTask(DuplicatiOperation.ListRemote, backup)));

    private static Dto.TaskStartedDto ExecuteRepair(Connection connection, IBackup backup, IQueueRunnerService queueRunnerService, Dto.RepairInputDto? input)
        => DoRepair(connection, backup, false, queueRunnerService, input);

    private static Dto.TaskStartedDto ExecuteRepairUpdate(Connection connection, IBackup backup, IQueueRunnerService queueRunnerService, Dto.RepairInputDto? input)
        => DoRepair(connection, backup, true, queueRunnerService, input);

    private static Dto.TaskStartedDto ExecuteVacuum(IBackup backup, IQueueRunnerService queueRunnerService)
        => new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreateTask(DuplicatiOperation.Vacuum, backup)));

    private static Dto.TaskStartedDto ExecuteVerify(IBackup backup, IQueueRunnerService queueRunnerService)
        => new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreateTask(DuplicatiOperation.Verify, backup)));

    private static Dto.TaskStartedDto ExecuteCompact(IBackup backup, IQueueRunnerService queueRunnerService)
        => new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreateTask(DuplicatiOperation.Compact, backup)));

    private static Dto.TaskStartedDto DoRepair(Connection connection, IBackup backup, bool repairUpdate, IQueueRunnerService queueRunnerService, Dto.RepairInputDto? input)
    {
        // These are all props on the input object
        var extra = new Dictionary<string, string?>();
        if (input != null)
        {
            if (input.only_paths.HasValue)
                extra["repair-only-paths"] = input.only_paths.Value.ToString();
            if (!string.IsNullOrWhiteSpace(input.time))
            {
                extra["time"] = input.time;
                extra["ignore-update-if-version-exists"] = "true";
            }
            if (!string.IsNullOrWhiteSpace(input.version))
            {
                extra["version"] = input.version;
                extra["ignore-update-if-version-exists"] = "true";
            }

            // If the call explicitly asks for a refresh lock info, we use that
            if (input.refresh_lock_info != null)
                extra["repair-refresh-lock-info"] = input.refresh_lock_info.Value.ToString();
        }

        if (!extra.ContainsKey("repair-refresh-lock-info"))
        {
            var refresh = DetermineLockRefresh(backup.Settings) ?? DetermineLockRefresh(connection.Settings);
            if (!string.IsNullOrWhiteSpace(refresh))
                extra["repair-refresh-lock-info"] = refresh;
        }

        var filters = input?.paths ?? [];

        return new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreateTask(repairUpdate ? DuplicatiOperation.RepairUpdate : DuplicatiOperation.Repair, backup, extra, filters)));
    }

    private static string? DetermineLockRefresh(IEnumerable<ISetting> settings)
    {
        var refresh = settings.FirstOrDefault(x => string.Equals(x.Name.TrimStart('-'), "repair-refresh-lock-info"));
        if (refresh != null)
            return refresh.Value ?? "true";

        var lock_duration = settings.FirstOrDefault(x => string.Equals(x.Name.TrimStart('-'), "remote-file-lock-duration"));
        if (lock_duration != null)
        {
            try
            {
                var ts = Library.Utility.Timeparser.ParseTimeSpan(lock_duration.Value);
                if (ts.Ticks > 0)
                    return "true";
            }
            catch
            {
                // We don't know what the lock duration is, but assume it is non-zero
                return "true";
            }
        }

        return null;
    }

    private static Dto.TaskStartedDto ExecuteRunBackup(IBackup backup, bool skipQueue, IQueueRunnerService queueRunnerService)
    {
        var t = queueRunnerService.GetCurrentTask();
        var bt = t?.BackupID;

        // Already running
        if (bt != null && backup.ID == bt)
            return new Dto.TaskStartedDto("OK", t!.TaskID);

        t = queueRunnerService.GetCurrentTasks().FirstOrDefault(x => x.BackupID == backup.ID);
        if (t != null)
            return new Dto.TaskStartedDto("OK", t.TaskID);

        return new Dto.TaskStartedDto("OK", queueRunnerService.AddTask(Runner.CreateTask(DuplicatiOperation.BackupOrSync, backup), skipQueue));
    }

    private static Dto.CreateBackupDto ExecuteCopyToTemp(IBackup backup, Connection connection)
    {
        var ipx = Serializer.Deserialize<Server.Database.Backup>(new StringReader(Newtonsoft.Json.JsonConvert.SerializeObject(backup)));

        using (var tf = new Library.Utility.TempFile())
            ipx.SetDBPath(tf);
        ipx.ID = null;

        var assignedId = connection.RegisterTemporaryBackup(ipx, null);
        return new Dto.CreateBackupDto(assignedId, ipx.IsTemporary);
    }

    private static async Task<IEnumerable<RestoreTaskConfigElementDto>> ExecuteRestoreTaskConfigAsync(Connection connection, IQueueRunnerService queueRunnerService, IBackup backup)
    {
        using var tempFolder = new Library.Utility.TempFolder();
        var r = await queueRunnerService.RunImmediatelyAsync(Runner.CreateRestoreControlFilesTask(backup, tempFolder, [Runner.TaskSetupFilename])).ConfigureAwait(false) as Library.Interface.IRestoreControlFilesResults;

        var restoredFile = r?.Files?.Where(x => x.EndsWith(Runner.TaskSetupFilename))?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(restoredFile) || !File.Exists(restoredFile))
            return [];

        using var sr = new StreamReader(restoredFile);
        var taskData = Serializer.Deserialize<Server.Serializable.ImportExportStructure[]>(sr);

        var result = new List<RestoreTaskConfigElementDto>();
        foreach (var item in taskData ?? [])
        {
            if (item.Backup == null)
                continue;

            item.Backup.ID = null;
            item.Backup.SetDBPath(null);

            var assignedId = connection.RegisterTemporaryBackup(item.Backup, item.Schedule);

            // Create a version without sensitive information for display purposes
            var tempBk = item.Backup.Clone();
            tempBk.RemoveSensitiveInformation();

            result.Add(new RestoreTaskConfigElementDto()
            {
                BackupId = assignedId,
                Name = tempBk.Name,
                DisplayNames = item.DisplayNames,
                AdditionalTargetUrls = (tempBk.AdditionalTargetURLs ?? []).Select(x => x.TargetUrl).ToList(),
                TargetURLDisplay = tempBk.TargetURL,
                Metadata = tempBk.Metadata
            });
        }
        return result;
    }

    private static ImportBackupOutputDto ExecuteImportFromTemp(ImportBackupFromTempDto input, Connection connection, IBackupListService backupListService)
    {
        var source = connection.GetTemporaryBackup(input.BackupId);
        if (source == null)
            throw new BadRequestException("No such temporary backup");

        var ipx = new ImportExportStructure()
        {
            CreatedByVersion = Library.AutoUpdater.UpdaterManager.SelfVersion.Version ?? "Unknown",
            Backup = (Server.Database.Backup)source.Value.Backup,
            Schedule = (Server.Database.Schedule?)source.Value.Schedule,
            DisplayNames = new Dictionary<string, string>()
        };

        using var tempfile = new Library.Utility.TempFile();
        var passphrase = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        File.WriteAllBytes(tempfile, BackupImportExportHandler.ExportToJSON(ipx, passphrase));

        var res = backupListService.Import(false, input.ImportMetadata, input.Direct, false, passphrase, tempfile, null);
        connection.UnregisterTemporaryBackup(source.Value.Backup);

        return res;
    }
}
