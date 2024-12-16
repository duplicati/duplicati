using System.Text.Json;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1.Backup;

public class BackupPutDelete : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/backup/{id}", ([FromServices] Connection connection, [FromRoute] string id, [FromBody] Dto.BackupAndScheduleInputDto input)
            => ExecutePut(GetBackup(connection, id), connection, input))
            .RequireAuthorization();

        group.MapDelete("/backup/{id}", ([FromServices] Connection connection, [FromServices] IWorkerThreadsManager workerThreadsManager, [FromServices] ICaptchaProvider captchaProvider, [FromServices] LiveControls liveControls, [FromServices] IHttpContextAccessor httpContextAccessor, [FromRoute] string id, [FromQuery(Name = "delete-remote-files")] bool? delete_remote_files, [FromQuery(Name = "delete-local-db")] bool? delete_local_db, [FromQuery(Name = "captcha-token")] string? captcha_token, [FromQuery(Name = "captcha-answer")] string? captcha_answer, [FromQuery] bool? force) =>
        {
            var res = ExecuteDelete(GetBackup(connection, id), workerThreadsManager, captchaProvider, liveControls, delete_remote_files ?? false, delete_local_db, captcha_token, captcha_answer, force ?? false);
            if (res.Status != "OK" && httpContextAccessor.HttpContext != null)
                httpContextAccessor.HttpContext.Response.StatusCode = 500;
            return res;
        })
        .RequireAuthorization();
    }

    private static IBackup GetBackup(Connection connection, string id)
        => connection.GetBackup(id) ?? throw new NotFoundException("Backup not found");

    private class WrappedBackup : Server.Database.Backup
    {
        public string? DBPathSetter
        {
            get => DBPath;
            set => SetDBPath(value);
        }
    }

    private static void ExecutePut(IBackup existing, Connection connection, Dto.BackupAndScheduleInputDto input)
    {
        // TODO: This method and "POST /backups" are 99% identical

        if (input.Backup == null)
            throw new BadRequestException("No backup data found in request body");

        var filters = input.Backup.Filters ?? Array.Empty<Dto.BackupAndScheduleInputDto.FilterInputDto>();
        var settings = input.Backup.Settings ?? Array.Empty<Dto.BackupAndScheduleInputDto.SettingInputDto>();

        try
        {
            var backup = new WrappedBackup()
            {
                ID = existing.ID,
                Name = input.Backup.Name,
                Description = input.Backup.Description,
                Tags = input.Backup.Tags,
                TargetURL = input.Backup.TargetURL,
                DBPathSetter = null,
                Sources = input.Backup.Sources,
                Settings = settings.Select(x => new Setting()
                {
                    Name = x.Name,
                    Value = x.Value,
                    Filter = x.Filter
                }).ToArray(),
                Filters = filters.Select(x => new Filter()
                {
                    Order = x.Order,
                    Include = x.Include,
                    Expression = x.Expression
                }).ToArray(),
                Metadata = input.Backup.Metadata ?? new Dictionary<string, string>()
            };

            var schedule = input.Schedule == null ? null : new Schedule()
            {
                ID = input.Schedule.ID,
                Tags = input.Schedule.Tags,
                Time = input.Schedule.Time ?? new DateTime(0),
                Repeat = input.Schedule.Repeat,
                LastRun = input.Schedule.LastRun ?? new DateTime(0),
                Rule = input.Schedule.Rule,
                AllowedDays = input.Schedule.AllowedDays
            };

            if (backup.IsTemporary && !existing.IsTemporary)
                throw new BadRequestException("Cannot update a temporary backup with a non-temporary backup");

            lock (connection.m_lock)
            {
                if (connection.Backups.Any(x => x.Name.Equals(backup.Name, StringComparison.OrdinalIgnoreCase) && x.ID != backup.ID))
                    throw new ConflictException($"There already exists a backup with the name: {backup.Name}");

                var err = connection.ValidateBackup(backup, schedule);
                if (!string.IsNullOrWhiteSpace(err))
                    throw new BadRequestException(err);

                //TODO: Merge in real passwords where the placeholder is found
                connection.AddOrUpdateBackupAndSchedule(backup, schedule);
            }
        }
        catch (Exception ex)
        {
            if (ex is UserReportedHttpException)
                throw;

            throw new ServerErrorException($"Unable to save backup or schedule: {ex.Message}");
        }
    }

    private static Dto.DeleteBackupOutputDto ExecuteDelete(IBackup backup, IWorkerThreadsManager workerThreadsManager, ICaptchaProvider captchaProvider, LiveControls liveControls, bool delete_remote_files, bool? delete_local_db, string? captcha_token, string? captcha_answer, bool force)
    {
        if (delete_remote_files)
        {
            if (string.IsNullOrWhiteSpace(captcha_token) || string.IsNullOrWhiteSpace(captcha_answer))
                throw new UnauthorizedException("Missing captcha");

            if (!captchaProvider.SolvedCaptcha(captcha_token, "DELETE /backup/" + backup.ID, captcha_answer))
                throw new ForbiddenException("Invalid captcha");
        }

        if (workerThreadsManager.WorkerThread!.Active)
        {
            try
            {
                //TODO: It's not safe to access the values like this, 
                //because the runner thread might interfere
                var nt = workerThreadsManager.WorkerThread.CurrentTask;
                if (backup.Equals(nt?.Backup))
                {
                    if (!force)
                        return new Dto.DeleteBackupOutputDto("failed", "backup-in-progress", nt?.TaskID);


                    bool hasPaused = liveControls.State != LiveControls.LiveControlState.Paused;
                    if (hasPaused)
                        liveControls.Pause(true);
                    nt.Abort();

                    for (int i = 0; i < 10; i++)
                        if (workerThreadsManager.WorkerThread.Active)
                        {
                            var t = workerThreadsManager.WorkerThread.CurrentTask;
                            if (backup.Equals(t == null ? null : t.Backup))
                                Thread.Sleep(1000);
                            else
                                break;
                        }
                        else
                            break;

                    if (workerThreadsManager.WorkerThread.Active)
                    {
                        var t = workerThreadsManager.WorkerThread.CurrentTask;
                        if (backup.Equals(t == null ? null : t.Backup))
                        {
                            if (hasPaused)
                                liveControls.Resume();

                            return new Dto.DeleteBackupOutputDto("failed", "backup-unstoppable", t?.TaskID);
                        }
                    }

                    if (hasPaused)
                        liveControls.Resume();
                }
            }
            catch (Exception ex)
            {
                return new Dto.DeleteBackupOutputDto("error", ex.Message, null);
            }
        }

        var extra = new Dictionary<string, string>();
        if (delete_local_db.HasValue)
            extra["delete-local-db"] = delete_local_db.Value.ToString();
        if (delete_remote_files)
            extra["delete-remote-files"] = "true";

        return new Dto.DeleteBackupOutputDto("OK", null, workerThreadsManager.AddTask(Runner.CreateTask(DuplicatiOperation.Delete, backup, extra)));
    }

}
