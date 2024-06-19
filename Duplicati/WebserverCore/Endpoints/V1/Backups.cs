using System.Text.Json;
using Duplicati.Library.RestAPI;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Backups : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/backups", ([FromServices] Connection connection)
            => ExecuteGet(connection))
               .RequireAuthorization();

        // TODO: Figure out why the JSON deserialization is not working here
        // group.MapPost("/backups", ([FromServices] Connection connection, [FromBody] Dto.BackupAndScheduleInputDto input, [FromQuery] bool? temporary, [FromQuery] bool? existingdb)
        //     => ExecuteAdd(connection, input, temporary ?? false, existingdb ?? false)).RequireAuthorization();

        group.MapPost("/backups", async ([FromServices] Connection connection, [FromQuery] bool? temporary, [FromQuery] bool? existingdb, [FromServices] IHttpContextAccessor httpContextAccessor) =>
            {
                var opts = new JsonSerializerOptions()
                {
                    Converters = { new DayOfWeekStringEnumConverter() }
                };
                var input = await JsonSerializer.DeserializeAsync<Dto.BackupAndScheduleInputDto>(httpContextAccessor.HttpContext!.Request.Body, opts)
                    ?? throw new BadRequestException("No data found in request body");
                return ExecuteAdd(connection, input, temporary ?? false, existingdb ?? false);
            })
            .RequireAuthorization();

        // TODO: This is still using form due to file upload, but should be fixed as we are only sending a small file that could be base64 encoded
        group.MapPost("/backups/import", ([FromForm] IFormFile config, [FromForm] bool? cmdline, [FromForm] bool? import_metadata, [FromForm] bool? direct, [FromForm] string? callback, [FromForm] string? passphrase, [FromForm] string access_token, [FromServices] IJWTTokenProvider jWTTokenProvider, [FromServices] Connection connection, [FromServices] IHttpContextAccessor httpContextAccessor) =>
        {
            // Manually verify the access token
            if (jWTTokenProvider.ReadAccessToken(access_token) == null)
                throw new UnauthorizedException("Invalid access token");

            var html = ExecuteImport(connection, cmdline ?? false, import_metadata ?? false, direct ?? false, callback ?? "", passphrase ?? "", config);
            httpContextAccessor.HttpContext!.Response.ContentType = "text/html";
            return html;
        });
    }

    private static IEnumerable<Dto.BackupAndScheduleOutputDto> ExecuteGet(Connection connection)
    {
        var schedules = connection.Schedules;
        var backups = connection.Backups;

        var all = backups.Select(n => new
        {
            IsUnencryptedOrPassphraseStored = connection.IsUnencryptedOrPassphraseStored(long.Parse(n.ID)),
            Backup = n,
            Schedule = schedules.FirstOrDefault(x => x.Tags != null && x.Tags.Contains("ID=" + n.ID))
        });

        return all.Select(x => new Dto.BackupAndScheduleOutputDto()
        {
            Backup = new Dto.BackupDto()
            {
                ID = x.Backup.ID,
                Name = x.Backup.Name,
                Description = x.Backup.Description,
                IsTemporary = x.Backup.IsTemporary,
                IsUnencryptedOrPassphraseStored = x.IsUnencryptedOrPassphraseStored,
                Metadata = x.Backup.Metadata,
                Sources = x.Backup.Sources,
                Settings = x.Backup.Settings?.Select(y => new Dto.SettingDto()
                {
                    Name = y.Name,
                    Value = y.Value,
                    Filter = y.Filter,
                    Argument = y.Argument
                }),
                Filters = x.Backup.Filters?.Select(y => new Dto.FilterDto()
                {
                    Order = y.Order,
                    Include = y.Include,
                    Expression = y.Expression,
                }),
                TargetURL = x.Backup.TargetURL,
                DBPath = x.Backup.DBPath,
                Tags = x.Backup.Tags
            },
            Schedule = x.Schedule == null ? null : new Dto.ScheduleDto()
            {
                ID = x.Schedule.ID,
                Tags = x.Schedule.Tags,
                Time = x.Schedule.Time,
                Repeat = x.Schedule.Repeat,
                LastRun = x.Schedule.LastRun,
                Rule = x.Schedule.Rule,
                AllowedDays = x.Schedule.AllowedDays
            }
        });
    }
    private static string ExecuteImport(Connection connection, bool cmdline, bool import_metadata, bool direct, string callback, string passphrase, IFormFile? file)
    {
        var output_template = "<html><body><script type=\"text/javascript\">var jso = 'JSO'; var rp = null; try { rp = parent['CBM']; } catch (e) {}; if (rp) { rp('MSG', jso); } else { alert; rp('MSG'); };</script></body></html>";
        //output_template = "<html><body><script type=\"text/javascript\">alert('MSG');</script></body></html>";
        try
        {
            output_template = output_template.Replace("CBM", callback);
            if (cmdline)
                throw new TextOutputErrorException("Import from commandline not yet implemented");

            if (file == null)
                throw new TextOutputErrorException("No file uploaded");

            using var tempfile = new Library.Utility.TempFile();
            using (var fs = File.OpenWrite(tempfile))
                file.CopyTo(fs);

            var ipx = BackupImportExportHandler.LoadConfiguration(tempfile, import_metadata, () => passphrase);
            if (direct)
            {
                lock (connection.m_lock)
                {
                    var basename = ipx.Backup.Name;
                    var c = 0;
                    while (c++ < 100 && connection.Backups.Any(x => x.Name.Equals(ipx.Backup.Name, StringComparison.OrdinalIgnoreCase)))
                        ipx.Backup.Name = basename + " (" + c.ToString() + ")";

                    if (connection.Backups.Any(x => x.Name.Equals(ipx.Backup.Name, StringComparison.OrdinalIgnoreCase)))
                        return output_template.Replace("MSG", $"There already exists a backup with the name: {basename}");

                    var err = connection.ValidateBackup(ipx.Backup, ipx.Schedule);
                    if (!string.IsNullOrWhiteSpace(err))
                        return output_template.Replace("MSG", err);

                    connection.AddOrUpdateBackupAndSchedule(ipx.Backup, ipx.Schedule);
                }

                return output_template.Replace("MSG", "OK");
            }
            else
            {
                return output_template
                    .Replace("'JSO'", JsonSerializer.Serialize(ipx))
                    .Replace("MSG", "Import completed, but a browser issue prevents loading the contents. Try using the direct import method instead.");
            }

        }
        catch (Exception ex)
        {
            connection.LogError("", "Failed to import backup", ex);
            throw new TextOutputErrorException(output_template.Replace("MSG", ex.Message.Replace("\'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n")), contentType: "text/html");
        }
    }

    private class WrappedBackup : Server.Database.Backup
    {
        public string? DBPathSetter
        {
            get => DBPath;
            set => SetDBPath(value);
        }
    }

    private static Dto.CreateBackupDto ExecuteAdd(Connection connection, Dto.BackupAndScheduleInputDto data, bool temporary, bool existingDb)
    {
        try
        {
            if (data.Backup == null)
                throw new BadRequestException("Data object had no backup entry");

            var filters = data.Backup.Filters ?? Array.Empty<Dto.BackupAndScheduleInputDto.FilterInputDto>();
            var settings = data.Backup.Settings ?? Array.Empty<Dto.BackupAndScheduleInputDto.SettingInputDto>();

            var backup = new WrappedBackup()
            {
                ID = null,
                Name = data.Backup.Name,
                Description = data.Backup.Description,
                Tags = data.Backup.Tags,
                TargetURL = data.Backup.TargetURL,
                DBPathSetter = null,
                Sources = data.Backup.Sources,
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
                Metadata = data.Backup.Metadata ?? new Dictionary<string, string>()
            };

            var schedule = data.Schedule == null ? null : new Schedule()
            {
                ID = data.Schedule.ID,
                Tags = data.Schedule.Tags,
                Time = data.Schedule.Time ?? new DateTime(0),
                Repeat = data.Schedule.Repeat,
                LastRun = data.Schedule.LastRun ?? new DateTime(0),
                Rule = data.Schedule.Rule,
                AllowedDays = data.Schedule.AllowedDays
            };

            if (temporary)
            {
                using (var tf = new Library.Utility.TempFile())
                    backup.DBPathSetter = tf;

                connection.RegisterTemporaryBackup(backup);
            }
            else
            {
                if (existingDb)
                {
                    backup.DBPathSetter = Library.Main.DatabaseLocator.GetDatabasePath(data.Backup.TargetURL, null, false, false);
                    if (string.IsNullOrWhiteSpace(data.Backup.DBPath))
                        throw new Exception("Unable to find remote db path?");
                }

                lock (connection.m_lock)
                {
                    if (connection.Backups.Any(x => x.Name.Equals(data.Backup.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new ConflictException($"There already exists a backup with the name: {data.Backup.Name}");

                    var err = connection.ValidateBackup(backup, schedule);
                    if (!string.IsNullOrWhiteSpace(err))
                        throw new BadRequestException(err);

                    connection.AddOrUpdateBackupAndSchedule(backup, schedule);
                }
            }

            return new Dto.CreateBackupDto(backup.ID, backup.IsTemporary);
        }
        catch (Exception ex)
        {
            if (data == null)
                throw new BadRequestException($"Data object was null: {ex.Message}");

            if (ex is UserReportedHttpException)
                throw;

            throw new ServerErrorException($"Unable to save schedule or backup object: {ex.Message}");
        }
    }
}
