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
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1.Backup;

public class BackupGet : IEndpointV1
{
    private record GetBackupResultDto(Dto.ScheduleDto? Schedule, Dto.BackupDto Backup, Dictionary<string, string> DisplayNames);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/backup/{id}", ([FromServices] Connection connection, [FromRoute] string id)
            => ExecuteGet(connection, GetBackup(connection, id)))
            .RequireAuthorization();

        group.MapGet("/backup/{id}/files", ([FromServices] Connection connection, [FromRoute] string id, [FromQuery] string? filter, [FromQuery] string? time, [FromQuery(Name = "all-versions")] bool? allVersions, [FromQuery(Name = "prefix-only")] bool? prefixOnly, [FromQuery(Name = "folder-contents")] bool? folderContents)
            => ExecuteGetFiles(GetBackup(connection, id), filter, time, allVersions ?? false, prefixOnly ?? false, folderContents ?? false, new Dictionary<string, string>()))
            .RequireAuthorization();

        group.MapGet("/backup/{id}/log", ([FromServices] Connection connection, [FromRoute] string id, [FromQuery] long? offset, [FromQuery] long? pagesize)
            => ExecuteGetLog(connection, GetBackup(connection, id), offset, pagesize ?? 100))
            .RequireAuthorization();

        group.MapGet("/backup/{id}/remotelog", ([FromServices] Connection connection, [FromRoute] string id, [FromQuery] long? offset, [FromQuery] long? pagesize)
            => ExecuteGetRemotelog(connection, GetBackup(connection, id), offset, pagesize ?? 100))
            .RequireAuthorization();

        group.MapGet("/backup/{id}/filesets", ([FromServices] Connection connection, [FromRoute] string id, [FromQuery(Name = "include-metadata")] bool? includeMetadata, [FromQuery(Name = "from-remote-only")] bool? fromRemoteOnly)
            => ExecuteGetFilesets(GetBackup(connection, id), includeMetadata ?? false, fromRemoteOnly ?? false))
            .RequireAuthorization();

        group.MapGet("/backup/{id}/export-argsonly", ([FromServices] Connection connection, [FromRoute] string id, [FromQuery(Name = "export-passwords")] bool? exportPasswords, [FromQuery] string? passphrase)
            => ExecuteGetExportArgsOnly(GetBackup(connection, id), exportPasswords ?? false))
            .RequireAuthorization();

        group.MapGet("/backup/{id}/export-cmdline", ([FromServices] Connection connection, [FromRoute] string id, [FromQuery(Name = "export-passwords")] bool? exportPasswords, [FromQuery] string? passphrase)
            => ExecuteGetExportCmdline(GetBackup(connection, id), exportPasswords ?? false))
            .RequireAuthorization();

        group.MapGet("/backup/{id}/export", ([FromServices] Connection connection, [FromServices] IHttpContextAccessor httpContextAccessor, [FromServices] IJWTTokenProvider jWTTokenProvider, [FromRoute] string id, [FromQuery(Name = "export-passwords")] bool? exportPasswords, [FromQuery] string? passphrase, [FromQuery] string token, CancellationToken ct) =>
        {
            // Custom authorization check
            var singleOperationToken = jWTTokenProvider.ReadSingleOperationToken(token);
            if (singleOperationToken.Operation != "export")
                throw new UnauthorizedException("Invalid operation");

            var (data, filename) = ExecuteGetExport(connection, GetBackup(connection, id), exportPasswords ?? false, passphrase);
            var resp = httpContextAccessor.HttpContext!.Response;

            resp.ContentLength = data.Length;
            resp.ContentType = "application/octet-stream";
            resp.Headers.Append("Content-Disposition", $"attachment; filename={filename}");
            resp.Body.WriteAsync(data, ct);
        });

        group.MapGet("/backup/{id}/isdbusedelsewhere", ([FromServices] Connection connection, [FromRoute] string id)
            => ExecuteGetIsdbUsedElsewhere(GetBackup(connection, id)))
            .RequireAuthorization();

        group.MapGet("/backup/{id}/isactive", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromRoute] string id)
            => ExecuteGetIsActive(workerThreadsManager, GetBackup(connection, id)))
            .RequireAuthorization();
    }

    private static IBackup GetBackup(Connection connection, string id)
        => connection.GetBackup(id) ?? throw new NotFoundException("Backup not found");

    private static GetBackupResultDto ExecuteGet(Connection connection, IBackup bk)
    {
        var scheduleId = connection.GetScheduleIDsFromTags(new string[] { "ID=" + bk.ID });
        var schedule = scheduleId.Any() ? connection.GetSchedule(scheduleId.First()) : null;
        var sourcenames = SpecialFolders.GetSourceNames(bk);

        //TODO: Filter out the password in both settings and the target url

        return new GetBackupResultDto(
            schedule == null ? null : new Dto.ScheduleDto()
            {
                ID = schedule.ID,
                Tags = schedule.Tags,
                Time = schedule.Time,
                Repeat = schedule.Repeat,
                LastRun = schedule.LastRun,
                Rule = schedule.Rule,
                AllowedDays = schedule.AllowedDays
            },
            new Dto.BackupDto()
            {
                ID = bk.ID,
                Name = bk.Name,
                Sources = bk.Sources,
                Settings = bk.Settings?.Select(x => new Dto.SettingDto()
                {
                    Name = x.Name,
                    Value = x.Value,
                    Filter = x.Filter,
                    Argument = x.Argument
                }).ToArray(),
                Filters = bk.Filters?.Select(x => new Dto.FilterDto()
                {
                    Order = x.Order,
                    Include = x.Include,
                    Expression = x.Expression
                }).ToArray(),
                Metadata = bk.Metadata,
                Description = bk.Description,
                Tags = bk.Tags,
                TargetURL = bk.TargetURL,
                DBPath = bk.DBPath,
                IsTemporary = bk.IsTemporary,
                IsUnencryptedOrPassphraseStored = false,
            },
            sourcenames
        );
    }

    private static Dictionary<string, object> SearchFiles(IBackup backup, string? filter, string? timestring, bool allVersions, bool prefixOnly, bool folderContents, Dictionary<string, string> extraValues)
    {
        if (string.IsNullOrWhiteSpace(timestring) && !allVersions)
            throw new BadRequestException("Invalid or missing time");

        var time = new DateTime();
        if (!allVersions)
            time = Library.Utility.Timeparser.ParseTimeInterval(timestring, DateTime.Now);

        var r = Runner.Run(Runner.CreateListTask(backup, [filter], prefixOnly, allVersions, folderContents, time), false) as Duplicati.Library.Interface.IListResults;
        if (r == null)
            throw new ServerErrorException("No result from list operation");

        var result = new Dictionary<string, object>();

        foreach (var k in extraValues)
            result[k.Key] = k.Value;

        result["Filesets"] = r.Filesets;
        result["Files"] = r.Files
            // Group directories first - support either directory separator here as we may be restoring data from an alternate platform
            .OrderByDescending(f => (f.Path.StartsWith('/') && f.Path.EndsWith('/')) || (!f.Path.StartsWith('/') && f.Path.EndsWith('\\')))
            // Sort both groups (directories and files) alphabetically
            .ThenBy(f => f.Path);

        return result;
    }

    private static Dictionary<string, object> ExecuteGetFiles(IBackup bk, string? filter, string? timestring, bool allVersions, bool prefixOnly, bool folderContents, Dictionary<string, string> extraValues)
        => SearchFiles(bk, filter, timestring, allVersions, prefixOnly, folderContents, extraValues);

    private static List<Dictionary<string, object>> ExecuteGetLog(Connection connection, IBackup bk, long? offset, long pagesize)
    {
        if (!File.Exists(bk.DBPath))
            return new List<Dictionary<string, object>>();

        using (var con = Library.SQLiteHelper.SQLiteLoader.LoadConnection(bk.DBPath))
        using (var cmd = con.CreateCommand())
            return LogData.DumpTable(cmd, "LogData", "ID", offset, pagesize);
    }

    private static List<Dictionary<string, object>> ExecuteGetRemotelog(Connection connection, IBackup bk, long? offset, long pagesize)
    {
        if (!File.Exists(bk.DBPath))
            return new List<Dictionary<string, object>>();

        using (var con = Library.SQLiteHelper.SQLiteLoader.LoadConnection(bk.DBPath))
        using (var cmd = con.CreateCommand())
        {
            var dt = LogData.DumpTable(cmd, "RemoteOperation", "ID", offset, pagesize);

            // Unwrap raw data to a string
            foreach (var n in dt)
                try { n["Data"] = System.Text.Encoding.UTF8.GetString((byte[])n["Data"]); }
                catch { }

            return dt;
        }
    }

    private static IEnumerable<IListResultFileset> ExecuteGetFilesets(IBackup bk, bool includeMetadata, bool fromRemoteOnly)
    {
        var extra = new Dictionary<string, string>
        {
            ["list-sets-only"] = "true"
        };
        if (includeMetadata)
            extra["list-sets-only"] = "false";
        if (fromRemoteOnly)
            extra["no-local-db"] = "true";

        var r = Runner.Run(Runner.CreateTask(DuplicatiOperation.List, bk, extra), false) as IListResults;
        if (r == null)
            throw new ServerErrorException("No result from list operation");

        if (r.EncryptedFiles && bk.Settings.Any(x => string.Equals("--no-encryption", x.Name, StringComparison.OrdinalIgnoreCase)))
            throw new ServerErrorException("encrypted-storage");

        return r.Filesets;
    }

    public static void RemovePasswords(IBackup backup)
    {
        backup.SanitizeSettings();
        backup.SanitizeTargetUrl();
    }

    private static Dto.ExportCommandlineDto ExecuteGetExportCmdline(IBackup backup, bool exportPasswords)
    {
        if (!exportPasswords)
            RemovePasswords(backup);

        return new Dto.ExportCommandlineDto(Runner.GetCommandLine(Runner.CreateTask(DuplicatiOperation.Backup, backup)));
    }

    private static Dto.ExportArgsOnlyDto ExecuteGetExportArgsOnly(IBackup backup, bool exportPasswords)
    {
        if (!exportPasswords)
            RemovePasswords(backup);

        var parts = Runner.GetCommandLineParts(Runner.CreateTask(DuplicatiOperation.Backup, backup));
        return new Dto.ExportArgsOnlyDto(
            parts.First(),
            parts.Skip(1).Where(x => !x.StartsWith("--", StringComparison.Ordinal)),
            parts.Skip(1).Where(x => x.StartsWith("--", StringComparison.Ordinal))
        );
    }

    private static (byte[] Data, string Filename) ExecuteGetExport(Connection connection, IBackup backup, bool exportPasswords, string? passphrase)
    {
        if (!exportPasswords)
            RemovePasswords(backup);

        byte[] data = EncodeDataForExport(connection, backup, passphrase);

        string filename = Uri.EscapeDataString(backup.Name + "-duplicati-config.json");
        if (!string.IsNullOrWhiteSpace(passphrase))
            filename += ".aes";

        return (data, filename);
    }

    private static byte[] EncodeDataForExport(Connection connection, IBackup bk, string? passphrase)
    {
        var ipx = connection.PrepareBackupForExport(bk);

        using var ms = new MemoryStream();
        JsonSerializer.Serialize(ms, ipx, new JsonSerializerOptions()
        {
            WriteIndented = true,
            Converters = {
                new JsonStringEnumConverter(),
                new DayOfWeekStringEnumConverter()
            }
        });

        if (string.IsNullOrWhiteSpace(passphrase))
            return ms.ToArray();

        ms.Position = 0;

        using var ms2 = new MemoryStream();
        using (var m = new Library.Encryption.AESEncryption(passphrase, new Dictionary<string, string>()))
            m.Encrypt(ms, ms2);

        return ms2.ToArray();
    }

    private static Dto.IsDbUsedElsewhereDto ExecuteGetIsdbUsedElsewhere(IBackup bk)
        => new Dto.IsDbUsedElsewhereDto(Library.Main.CLIDatabaseLocator.IsDatabasePathInUse(bk.DBPath));

    private static Dto.IsBackupActiveDto ExecuteGetIsActive(IWorkerThreadsManager workerThreadsManager, IBackup bk)
    {
        if (workerThreadsManager.WorkerThread == null)
            throw new InvalidOperationException("Worker thread not available");

        var t = workerThreadsManager.WorkerThread.CurrentTask;
        var bt = t?.Backup;
        if (bt != null && bk.ID == bt.ID)
            return new Dto.IsBackupActiveDto("OK", true);

        if (workerThreadsManager.WorkerThread.CurrentTasks.Any(x => x?.Backup == null || x.Backup.ID == bk.ID))
            return new Dto.IsBackupActiveDto("OK", true);

        return new Dto.IsBackupActiveDto("OK", false);
    }
}
