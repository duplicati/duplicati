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
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Provides methods for handling stored backups
/// </summary>
/// <param name="connection">The database connection used to access backup information.</param>
public class BackupListService(Connection connection) : IBackupListService
{
    /// <summary>
    /// A wrapper class for the Backup entity to allow setting the DBPath property
    /// </summary>
    private class WrappedBackup : Backup
    {
        /// <summary>
        /// Gets or sets the database path.
        /// </summary>
        public string? DBPathSetter
        {
            get => DBPath;
            set => SetDBPath(value);
        }
    }

    /// <inheritdoc/>
    public CreateBackupDto Add(BackupAndScheduleInputDto data, bool temporary, bool existingDb)
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
                    backup.DBPathSetter = Library.Main.CLIDatabaseLocator.GetDatabasePathForCLI(data.Backup.TargetURL, null, false, false);
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

    /// <inheritdoc/>
    public ImportBackupOutputDto Import(bool cmdline, bool import_metadata, bool direct, string passphrase, string tempfile)
    {
        try
        {
            if (cmdline)
                throw new BadRequestException("Import from commandline not yet implemented");

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
                        throw new BadRequestException("There already exists a backup with that name");

                    var err = connection.ValidateBackup(ipx.Backup, ipx.Schedule);
                    if (!string.IsNullOrWhiteSpace(err))
                        throw new BadRequestException(err);

                    connection.AddOrUpdateBackupAndSchedule(ipx.Backup, ipx.Schedule);
                }

                return new ImportBackupOutputDto(ipx.Backup.ID, null);
            }
            else
            {
                return new ImportBackupOutputDto(null, ipx);
            }
        }
        catch (Exception ex)
        {
            connection.LogError("", "Failed to import backup", ex);
            throw new ServerErrorException($"Failed to import backup: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public IEnumerable<BackupAndScheduleOutputDto> List(string? orderBy)
    {
        var schedules = connection.Schedules;
        var backups = connection.Backups.AsEnumerable();

        IEnumerable<Dto.BackupAndScheduleOutputDto> ApplySorting(IEnumerable<Dto.BackupAndScheduleOutputDto> backups, string sort)
        {
            var asc = true;
            sort = sort.ToLowerInvariant().Trim();
            if (sort.StartsWith("-"))
            {
                asc = false;
                sort = sort.Substring(1);
            }
            else if (sort.StartsWith("+"))
            {
                asc = true;
                sort = sort.Substring(1);
            }

            Func<Dto.BackupAndScheduleOutputDto, object?>? selector = sort switch
            {
                "name" => x => x.Backup.Name,
                "id" => x => x.Backup.ID,
                "lastrun" => x =>
                {
                    if (x.Backup.Metadata == null)
                        return null;
                    x.Backup.Metadata.TryGetValue("LastBackupStarted", out var res);
                    return res;
                }
                ,
                "nextrun" => x => x.Schedule?.Time,
                "schedule" => x => string.IsNullOrWhiteSpace(x.Schedule?.Repeat),
                "backend" => x => Library.Utility.Utility.GuessScheme(x.Backup.TargetURL),
                "sourcesize" => x =>
                {
                    if (x.Backup.Metadata == null)
                        return null;
                    x.Backup.Metadata.TryGetValue("SourceFilesSize", out var res);
                    if (long.TryParse(res, out var l))
                        return l;
                    return null;
                }
                ,
                "destinationsize" => x =>
                {
                    if (x.Backup.Metadata == null)
                        return null;
                    x.Backup.Metadata.TryGetValue("TargetFilesSize", out var res);
                    if (long.TryParse(res, out var l))
                        return l;
                    return null;
                }
                ,
                "duration" => x =>
                {
                    if (x.Backup.Metadata == null)
                        return null;
                    x.Backup.Metadata.TryGetValue("LastBackupDuration", out var res);
                    return res;
                }
                ,
                _ => null
            };

            // Ignore unknown sort fields
            if (selector != null)
            {
                if (backups is IOrderedEnumerable<Dto.BackupAndScheduleOutputDto> backupsOrdered)
                {
                    return asc
                        ? backupsOrdered.ThenBy(selector)
                        : backupsOrdered.ThenByDescending(selector);
                }
                else
                {
                    return asc
                        ? backups.OrderBy(selector)
                        : backups.OrderByDescending(selector);
                }
            }

            return backups;
        }

        var all = backups.Select(n => new
        {
            IsUnencryptedOrPassphraseStored = connection.IsUnencryptedOrPassphraseStored(long.Parse(n.ID)),
            Backup = n,
            Schedule = schedules.FirstOrDefault(x => x.Tags != null && x.Tags.Contains("ID=" + n.ID))
        });

        var res = all.Select(x => new Dto.BackupAndScheduleOutputDto()
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
                DBPathExists = File.Exists(x.Backup.DBPath),
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

        // Use DB setting if not set
        if (string.IsNullOrWhiteSpace(orderBy))
            orderBy = connection.ApplicationSettings.BackupListSortOrder;

        // Apply sorting, if any
        if (!string.IsNullOrWhiteSpace(orderBy))
            foreach (var direction in orderBy.Split(","))
                res = ApplySorting(res, direction);

        return res;
    }
}