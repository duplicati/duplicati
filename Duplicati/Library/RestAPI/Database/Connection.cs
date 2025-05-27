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

using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Server.Serialization.Interface;
using System.Text;
using Duplicati.Library.RestAPI;
using Duplicati.Library.Encryption;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Main;
using Duplicati.Library.AutoUpdater;
using System.Data;
using Duplicati.Library.Main.Database;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

#nullable enable

namespace Duplicati.Server.Database
{
    public class Connection : IDisposable
    {
        /// <summary>
        /// The placeholder for passwords in the UI
        /// </summary>
        public const string PASSWORD_PLACEHOLDER = "**********";

        private readonly IDbConnection m_connection;
        private readonly IDbCommand m_errorcmd;
        public readonly object m_lock = new object();
        public const int ANY_BACKUP_ID = -1;
        public const int SERVER_SETTINGS_ID = -2;
        private readonly Dictionary<string, Backup> m_temporaryBackups = new Dictionary<string, Backup>();
        private readonly bool m_encryptSensitiveFields;
        private readonly EncryptedFieldHelper.KeyInstance? m_key;
        private IServiceProvider? m_serviceProvider;
        private INotificationUpdateService? m_notificationUpdateService;
        private EventPollNotify? m_eventPollNotifyer;
        private readonly string m_dataFolder;

        private static readonly HashSet<string> _encryptedFields =
            BackendLoader.Backends.SelectMany(x => x.SupportedCommands ?? [])
                .Concat(EncryptionLoader.Modules.SelectMany(x => x.SupportedCommands ?? []))
                .Concat(CompressionLoader.Modules.SelectMany(x => x.SupportedCommands ?? []))
                .Concat(GenericLoader.Modules.SelectMany(x => x.SupportedCommands ?? []))
                .Concat(WebLoader.Modules.SelectMany(x => x.SupportedCommands ?? []))
                .Concat(new Options(new Dictionary<string, string>()).SupportedCommands)
                .Where(x => x.Type == Library.Interface.CommandLineArgument.ArgumentType.Password)
                .SelectMany(x => new string[] { x.Name }.Concat(x.Aliases ?? []))
                .SelectMany(x => new string[] { x, $"--{x}" })
                .Concat([
                    ServerSettings.CONST.JWT_CONFIG,
                    ServerSettings.CONST.PBKDF_CONFIG,
                    ServerSettings.CONST.REMOTE_CONTROL_CONFIG,
                    ServerSettings.CONST.SERVER_SSL_CERTIFICATE,
                    ServerSettings.CONST.SERVER_SSL_CERTIFICATEPASSWORD
                ])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public Connection(IDbConnection connection, bool disableFieldEncryption, EncryptedFieldHelper.KeyInstance? key, string dataFolder, Action startOrStopUsageReporter)
        {
            m_dataFolder = dataFolder;
            m_encryptSensitiveFields = !disableFieldEncryption;
            m_key = key;
            m_connection = connection;
            m_errorcmd = m_connection.CreateCommand(@"INSERT INTO ""ErrorLog"" (""BackupID"", ""Message"", ""Exception"", ""Timestamp"") VALUES (@BackupId,@Message,@Exception,@Timestamp)");

            this.ApplicationSettings = new ServerSettings(this, startOrStopUsageReporter);
        }

        /// <summary>
        /// The service provider is used to resolve dependencies
        /// </summary>
        internal IServiceProvider? ServiceProvider => m_serviceProvider;

        /// <summary>
        /// Set the service provider to be used for resolving dependencies
        /// </summary>
        /// <param name="sp">The service provider</param>
        public void SetServiceProvider(IServiceProvider sp)
        {
            m_serviceProvider = sp;
            m_notificationUpdateService = sp?.GetRequiredService<INotificationUpdateService>();
            m_eventPollNotifyer = sp?.GetRequiredService<EventPollNotify>();
        }

        public bool IsEncryptingFields => m_encryptSensitiveFields;

        public void ReWriteAllFieldsIfEncryptionChanged()
        {
            // The token is automatically decrypted when the settings are loaded
            // In case the password has changed, this will fail and return the encrypted
            // hex-string, but will crash before reaching this point
            if (this.ApplicationSettings.EncryptedFields != m_encryptSensitiveFields)
            {
                var backups = this.Backups;
                foreach (var b in backups)
                {
                    ((Backup)b).LoadChildren(this);
                    AddOrUpdateBackup(b, false, null);
                }

                this.SetSettings(this.GetSettings(ANY_BACKUP_ID), ANY_BACKUP_ID);
                this.ApplicationSettings.EncryptedFields = m_encryptSensitiveFields;
            }
        }

        public void SetPreloadSettingsIfChanged(Dictionary<string, string> newsettings)
        {
            if (newsettings == null || newsettings.Count == 0)
                return;

            var settingsHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(newsettings.OrderBy(x => x.Key)))));
            if (settingsHash == this.ApplicationSettings.PreloadSettingsHash)
                return;

            newsettings = newsettings
                .ToDictionary(x => x.Key.StartsWith("--") ? x.Key : $"--{x.Key}", x => x.Value);

            var currentSettings = this.Settings;
            var filters = currentSettings.Where(x => x.Filter != null).ToDictionary(x => x.Name, x => x.Filter);

            var updatedSettings = currentSettings
                .Where(x => !newsettings.ContainsKey(x.Name))
                .Concat(newsettings.Where(x => x.Value != null).Select(x => new Setting
                {
                    Name = x.Key,
                    Value = x.Value,
                    Filter = filters.GetValueOrDefault(x.Key) ?? ""
                }));

            this.Settings = updatedSettings.ToArray();
            this.ApplicationSettings.PreloadSettingsHash = settingsHash;
        }

        public void LogError(string? backupid, string message, Exception ex)
        {
            lock (m_lock)
            {
                if (!long.TryParse(backupid, out long id))
                    id = -1;

                m_errorcmd.SetParameterValue("@BackupId", id)
                    .SetParameterValue("@Message", message)
                    .SetParameterValue("@Exception", ex?.ToString())
                    .SetParameterValue("@Timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow))
                    .ExecuteNonQuery();
            }
        }

        public void ExecuteWithCommand(Action<IDbCommand> f)
        {
            lock (m_lock)
                using (var cmd = m_connection.CreateCommand())
                    f(cmd);
        }

        public Serializable.ImportExportStructure PrepareBackupForExport(IBackup backup)
        {
            var scheduleId = GetScheduleIDsFromTags(new string[] { "ID=" + backup.ID });
            return new Serializable.ImportExportStructure()
            {
                CreatedByVersion = UpdaterManager.SelfVersion.Version ?? "Unknown",
                Backup = (Database.Backup)backup,
                Schedule = scheduleId != null && scheduleId.Any() ? (Schedule?)GetSchedule(scheduleId.First()) : null,
                DisplayNames = SpecialFolders.GetSourceNames(backup)
            };
        }

        public string RegisterTemporaryBackup(IBackup backup)
        {
            lock (m_lock)
            {
                if (backup == null)
                    throw new ArgumentNullException(nameof(backup));
                if (backup.ID != null)
                    throw new ArgumentException("Backup is already active, cannot make temporary");

                backup.ID = Guid.NewGuid().ToString("D");
                m_temporaryBackups.Add(backup.ID, (Backup)backup);
                return backup.ID;
            }
        }

        public void UnregisterTemporaryBackup(IBackup backup)
        {
            lock (m_lock)
                m_temporaryBackups.Remove(backup.ID);
        }

        public void UpdateTemporaryBackup(IBackup backup)
        {
            lock (m_lock)
                if (m_temporaryBackups.Remove(backup.ID))
                    m_temporaryBackups.Add(backup.ID, (Backup)backup);
        }

        public IBackup? GetTemporaryBackup(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            lock (m_lock)
            {
                m_temporaryBackups.TryGetValue(id, out var b);
                return b;
            }
        }

        public ServerSettings ApplicationSettings { get; private set; }

        internal IDictionary<string, string?> GetMetadata(long id)
        {
            lock (m_lock)
                return ReadFromDb(
                    (rd) => new KeyValuePair<string, string?>(
                        ConvertToString(rd, 0) ?? "",
                        ConvertToString(rd, 1)
                    ),
                    cmd => cmd.SetCommandAndParameters(@"SELECT ""Name"", ""Value"" FROM ""Metadata"" WHERE ""BackupID"" = @Id")
                        .SetParameterValue("@Id", id)
                )
                .ToDictionary((k) => k.Key, (k) => k.Value);
        }

        internal void SetMetadata(IDictionary<string, string> values, long id, IDbTransaction? transaction)
        {
            lock (m_lock)
            {
                var tr = transaction ?? m_connection.BeginTransaction();
                OverwriteAndUpdateDb(
                    tr,
                    cmd => cmd.SetCommandAndParameters(@"DELETE FROM ""Metadata"" WHERE ""BackupID"" = @Id")
                        .SetParameterValue("@Id", id),
                    values ?? new Dictionary<string, string>(),
                    cmd => cmd.SetCommandAndParameters(@"INSERT INTO ""Metadata"" (""BackupID"", ""Name"", ""Value"") VALUES (@BackupId, @Name, @Value)"),
                    (cmd, f) => cmd.SetParameterValue("@BackupId", id)
                        .SetParameterValue("@Name", f.Key)
                        .SetParameterValue("@Value", f.Value)
                );

                if (transaction == null)
                {
                    tr.Commit();
                    tr.Dispose();
                }
            }
        }

        internal IFilter[] GetFilters(long id)
        {
            lock (m_lock)
                return ReadFromDb(
                    (rd) => (IFilter)new Filter()
                    {
                        Order = ConvertToInt64(rd, 0),
                        Include = ConvertToBoolean(rd, 1),
                        Expression = ConvertToString(rd, 2) ?? ""
                    },
                    cmd => cmd.SetCommandAndParameters(@"SELECT ""Order"", ""Include"", ""Expression"" FROM ""Filter"" WHERE ""BackupID"" = @Id ORDER BY ""Order"" ")
                        .SetParameterValue("@Id", id))
                    .ToArray();
        }

        internal void SetFilters(IEnumerable<IFilter> values, long id, IDbTransaction? transaction = null)
        {
            lock (m_lock)
            {
                var tr = transaction ?? m_connection.BeginTransaction();
                OverwriteAndUpdateDb(
                    tr,
                    cmd => cmd.SetCommandAndParameters(@"DELETE FROM ""Filter"" WHERE ""BackupID"" = @Id")
                        .SetParameterValue("@Id", id),
                    values,
                    cmd => cmd.SetCommandAndParameters(@"INSERT INTO ""Filter"" (""BackupID"", ""Order"", ""Include"", ""Expression"") VALUES (@Id, @Order, @Include, @Expression)"),
                    (cmd, f) => cmd.SetParameterValue("@Id", id)
                        .SetParameterValue("@Order", f.Order)
                        .SetParameterValue("@Include", f.Include)
                        .SetParameterValue("@Expression", f.Expression)
                );

                if (transaction == null)
                {
                    tr.Commit();
                    tr.Dispose();
                }
            }
        }

        public ISetting[] GetSettings(long id)
        {
            lock (m_lock)
                return ReadFromDb(
                    (rd) => (ISetting)new Setting()
                    {
                        Filter = ConvertToString(rd, 0) ?? "",
                        Name = ConvertToString(rd, 1) ?? "",
                        Value = DecryptSensitiveFields(ConvertToString(rd, 2) ?? "", m_key)
                        //TODO: Attach the argument information
                    },
                    cmd => cmd.SetCommandAndParameters(@"SELECT ""Filter"", ""Name"", ""Value"" FROM ""Option"" WHERE ""BackupID"" = @Id")
                        .SetParameterValue("@Id", id))
                    .ToArray();
        }

        internal void SetSettings(IEnumerable<ISetting> values, long id, IDbTransaction? transaction = null)
        {
            lock (m_lock)
            {
                var tr = transaction ?? m_connection.BeginTransaction();
                if (m_encryptSensitiveFields)
                    values = values.Select(x => new Setting
                    {
                        Filter = x.Filter,
                        Name = x.Name,
                        Value = EncryptSensitiveFields(x.Name, x.Value, m_key)
                    }).ToList();

                OverwriteAndUpdateDb(
                    tr,
                    cmd => cmd.SetCommandAndParameters(@"DELETE FROM ""Option"" WHERE ""BackupID"" = @Id")
                        .SetParameterValue("@Id", id),
                    values,
                    cmd => cmd.SetCommandAndParameters(@"INSERT INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"") VALUES (@BackupId, @Filter, @Name, @Value)"),
                    (cmd, f) =>
                    {
                        if (PASSWORD_PLACEHOLDER.Equals(f.Value))
                            throw new Exception("Attempted to save a property with the placeholder password");

                        cmd.SetParameterValue("@BackupId", id)
                            .SetParameterValue("@Filter", f.Filter ?? "")
                            .SetParameterValue("@Name", f.Name ?? "")
                            .SetParameterValue("@Value", f.Value ?? "");
                    }
                );

                if (transaction == null)
                {
                    tr.Commit();
                    tr.Dispose();
                }
            }
        }

        internal string?[] GetSources(long id)
        {
            lock (m_lock)
                return ReadFromDb(
                    (rd) => ConvertToString(rd, 0),
                    cmd => cmd.SetCommandAndParameters(@"SELECT ""Path"" FROM ""Source"" WHERE ""BackupID"" = @Id")
                        .SetParameterValue("@Id", id))
                    .ToArray();
        }

        internal void SetSources(IEnumerable<string> values, long id, IDbTransaction transaction)
        {
            lock (m_lock)
            {
                var tr = transaction ?? m_connection.BeginTransaction();
                OverwriteAndUpdateDb(
                    tr,
                    cmd => cmd.SetCommandAndParameters(@"DELETE FROM ""Source"" WHERE ""BackupID"" = @Id")
                        .SetParameterValue("@Id", id),
                    values,
                    cmd => cmd.SetCommandAndParameters(@"INSERT INTO ""Source"" (""BackupID"", ""Path"") VALUES (@BackupId, @Path)"),
                    (cmd, f) => cmd.SetParameterValue("@BackupId", id)
                        .SetParameterValue("@Path", f)
                );

                if (transaction == null)
                {
                    tr.Commit();
                    tr.Dispose();
                }
            }
        }

        internal long[] GetBackupIDsForTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
                return new long[0];

            if (tags.Length == 1 && tags[0].StartsWith("ID=", StringComparison.Ordinal))
                return new long[] { long.Parse(tags[0].Substring("ID=".Length)) };

            lock (m_lock)
                using (var cmd = m_connection.CreateCommand())
                {
                    var sb = new StringBuilder();

                    foreach (var t in tags)
                    {
                        if (sb.Length != 0)
                            sb.Append(" OR ");
                        sb.Append(@" (',' || ""Tags"" || ',' LIKE '%,' || ? || ',%') ");

                        var p = cmd.CreateParameter();
                        p.Value = t;
                        cmd.Parameters.Add(p);
                    }

                    cmd.SetCommandAndParameters(@"SELECT ""ID"" FROM ""Backup"" WHERE " + sb);

                    return Read(cmd, (rd) => ConvertToInt64(rd, 0)).ToArray();
                }
        }

        public IBackup? GetBackup(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            return long.TryParse(id, out long lid) ? GetBackup(lid) : GetTemporaryBackup(id);
        }

        internal IBackup? GetBackup(long id)
        {
            lock (m_lock)
            {
                var bk = ReadFromDb(
                    (rd) => new Backup
                    {
                        ID = ConvertToInt64(rd, 0).ToString(),
                        Name = ConvertToString(rd, 1),
                        Description = ConvertToString(rd, 2),
                        Tags = (ConvertToString(rd, 3) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                        TargetURL = EncryptedFieldHelper.Decrypt(ConvertToString(rd, 4), m_key),
                        DBPath = ConvertToString(rd, 5),
                    },
                    cmd => cmd.SetCommandAndParameters(@"SELECT ""ID"", ""Name"", ""Description"", ""Tags"", ""TargetURL"", ""DBPath"" FROM ""Backup"" WHERE ID = @Id")
                        .SetParameterValue("@Id", id))
                    .FirstOrDefault();

                if (bk != null)
                    bk.LoadChildren(this);

                return bk;
            }
        }

        public ISchedule? GetSchedule(long id)
        {
            lock (m_lock)
            {
                var bk = ReadFromDb(
                    (rd) => new Schedule
                    {
                        ID = ConvertToInt64(rd, 0),
                        Tags = (ConvertToString(rd, 1) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                        Time = ConvertToDateTime(rd, 2),
                        Repeat = ConvertToString(rd, 3),
                        LastRun = ConvertToDateTime(rd, 4),
                        Rule = ConvertToString(rd, 5),
                    },
                    cmd => cmd.SetCommandAndParameters(@"SELECT ""ID"", ""Tags"", ""Time"", ""Repeat"", ""LastRun"", ""Rule"" FROM ""Schedule"" WHERE ID = @Id")
                        .SetParameterValue("@Id", id))
                    .FirstOrDefault();

                return bk;
            }
        }

        public bool IsUnencryptedOrPassphraseStored(long id)
        {
            lock (m_lock)
            {
                var usesEncryption = ReadFromDb(
                    (rd) => ConvertToBoolean(rd, 0),
                    cmd => cmd.SetCommandAndParameters(@"SELECT VALUE != '' FROM ""Option"" WHERE BackupID = @Id AND NAME='encryption-module'")
                        .SetParameterValue("@Id", id))
                    .FirstOrDefault();

                if (!usesEncryption)
                {
                    return true;
                }

                return ReadFromDb(
                    (rd) => ConvertToBoolean(rd, 0),
                    cmd => cmd.SetCommandAndParameters(@"SELECT VALUE != '' FROM ""Option"" WHERE BackupID = @Id AND NAME='passphrase'")
                        .SetParameterValue("@Id", id))
                .FirstOrDefault();
            }
        }

        public long[] GetScheduleIDsFromTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
                return new long[0];

            lock (m_lock)
                using (var cmd = m_connection.CreateCommand())
                {
                    var sb = new StringBuilder();
                    var parameters = new Dictionary<string, object?>();
                    foreach ((var t, var i) in tags.Select((t, i) => (t, i)))
                    {
                        if (sb.Length != 0)
                            sb.Append(" OR ");
                        sb.Append(@$" (',' || ""Tags"" || ',' LIKE '%,' || @p{i} || ',%') ");
                        parameters.Add($"@p{i}", t);
                    }

                    cmd.SetCommandAndParameters(@"SELECT ""ID"" FROM ""Schedule"" WHERE " + sb);
                    cmd.SetParameterValues(parameters);

                    return Read(cmd, (rd) => ConvertToInt64(rd, 0)).ToArray();
                }
        }

        public void AddOrUpdateBackupAndSchedule(IBackup item, ISchedule? schedule)
        {
            AddOrUpdateBackup(item, true, schedule);
        }

        public string? ValidateBackup(IBackup item, ISchedule? schedule)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                return "Missing a name";

            if (string.IsNullOrWhiteSpace(item.TargetURL))
                return "Missing a target";

            if (item.Sources == null || item.Sources.Any(x => string.IsNullOrWhiteSpace(x)) || item.Sources.Length == 0)
                return "Invalid source list";

            var disabled_encryption = false;
            var passphrase = string.Empty;
            var gpgAsymmetricEncryption = false;
            if (item.Settings != null)
            {
                foreach (var s in item.Settings)

                    if (string.Equals(s.Name, "--no-encryption", StringComparison.OrdinalIgnoreCase))
                        disabled_encryption = string.IsNullOrWhiteSpace(s.Value) || Library.Utility.Utility.ParseBool(s.Value, false);
                    else if (string.Equals(s.Name, "passphrase", StringComparison.OrdinalIgnoreCase))
                        passphrase = s.Value;
                    else if (string.Equals(s.Name, "keep-versions", StringComparison.OrdinalIgnoreCase))
                    {
                        int i;
                        if (!int.TryParse(s.Value, out i) || i <= 0)
                            return "Retention value must be a positive integer";
                    }
                    else if (string.Equals(s.Name, "keep-time", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var ts = Library.Utility.Timeparser.ParseTimeSpan(s.Value);
                            if (ts <= TimeSpan.FromMinutes(5))
                                return "Retention value must be more than 5 minutes";
                        }
                        catch
                        {
                            return "Retention value must be a valid timespan";
                        }
                    }
                    else if (string.Equals(s.Name, "dblock-size", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var ds = Library.Utility.Sizeparser.ParseSize(s.Value);
                            if (ds < 1024 * 1024)
                                return "DBlock size must be at least 1MB";
                        }
                        catch
                        {
                            return "DBlock value must be a valid size string";
                        }
                    }
                    else if (string.Equals(s.Name, "--blocksize", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var ds = Library.Utility.Sizeparser.ParseSize(s.Value);
                            if (ds < 1024 || ds > int.MaxValue)
                                return "The blocksize must be at least 1KB";
                        }
                        catch
                        {
                            return "The blocksize value must be a valid size string";
                        }
                    }
                    else if (string.Equals(s.Name, "--prefix", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(s.Value) && s.Value.Contains("-"))
                            return "The prefix cannot contain hyphens (-)";
                    }
                    else if (string.Equals(s.Name, "--gpg-encryption-command", StringComparison.OrdinalIgnoreCase))
                    {
                        gpgAsymmetricEncryption = string.Equals(s.Value, "--encrypt", StringComparison.OrdinalIgnoreCase);
                    }
            }

            if (!disabled_encryption && !gpgAsymmetricEncryption && string.IsNullOrWhiteSpace(passphrase))
                return "Missing passphrase";

            if (schedule != null)
            {
                try
                {
                    var ts = Library.Utility.Timeparser.ParseTimeSpan(schedule.Repeat);
                    if (ts <= TimeSpan.FromMinutes(5))
                        return "Schedule repetition time must be more than 5 minutes";
                }
                catch
                {
                    return "Schedule repetition value must be a valid timespan";
                }

            }

            return null;
        }

        public void UpdateBackupDBPath(IBackup item, string path)
        {
            lock (m_lock)
            {
                using (var tr = m_connection.BeginTransaction())
                {
                    using (var cmd = m_connection.CreateCommand(tr, @"UPDATE ""Backup"" SET ""DBPath""= @Dbpath WHERE ""ID""= @Id"))
                    {
                        cmd.SetParameterValue("@Dbpath", path)
                            .SetParameterValue("@Id", item.ID)
                            .ExecuteNonQuery();
                        tr.Commit();
                    }
                }
            }

            m_notificationUpdateService?.IncrementLastDataUpdateId();
            m_eventPollNotifyer?.SignalNewEvent();
        }

        private void AddOrUpdateBackup(IBackup item, bool updateSchedule, ISchedule? schedule)
        {
            lock (m_lock)
            {
                bool update = item.ID != null;
                if (!update && item.DBPath == null)
                {
                    var folder = m_dataFolder;
                    if (!System.IO.Directory.Exists(folder))
                        System.IO.Directory.CreateDirectory(folder);

                    for (var i = 0; i < 100; i++)
                    {
                        var guess = System.IO.Path.Combine(folder, System.IO.Path.ChangeExtension(CLIDatabaseLocator.GenerateRandomName(), ".sqlite"));
                        if (!System.IO.File.Exists(guess))
                        {
                            ((Backup)item).DBPath = guess;
                            break;
                        }
                    }

                    if (item.DBPath == null)
                        throw new Exception("Unable to generate a unique database file name");
                }

                using (var tr = m_connection.BeginTransaction())
                {
                    OverwriteAndUpdateDb(
                        tr,
                        null,
                        [item],
                        cmd =>
                        {
                            if (update)
                                cmd.SetCommandAndParameters(@"UPDATE ""Backup"" SET ""Name""=@Name, ""Description""=@Description, ""Tags""=@Tags, ""TargetURL""=@TargetUrl WHERE ""ID""=@Id");
                            else
                                cmd.SetCommandAndParameters(@"INSERT INTO ""Backup"" (""Name"", ""Description"", ""Tags"", ""TargetURL"", ""DBPath"") VALUES (@Name,@Description,@Tags,@TargetUrl,@DbPath)");
                        },
                        (cmd, n) =>
                        {
                            if (n.TargetURL.IndexOf(PASSWORD_PLACEHOLDER, StringComparison.Ordinal) >= 0)
                                throw new Exception("Attempted to save a backup with the password placeholder");
                            if (update && long.Parse(n.ID) <= 0)
                                throw new Exception("Invalid update, cannot update application settings through update method");

                            cmd.SetParameterValue("@Name", n.Name)
                                .SetParameterValue("@Description", n.Description ?? "")
                                .SetParameterValue("@Tags", string.Join(",", n.Tags ?? new string[0]))
                                .SetParameterValue("@TargetUrl", m_encryptSensitiveFields ? EncryptedFieldHelper.Encrypt(n.TargetURL, m_key) : n.TargetURL);

                            if (update)
                                cmd.SetParameterValue("@Id", item.ID);
                            else
                                cmd.SetParameterValue("@DbPath", n.DBPath);
                        });

                    if (!update)
                        using (var cmd = m_connection.CreateCommand())
                        {
                            cmd.Transaction = tr;
                            item.ID = cmd.ExecuteScalarInt64(@"SELECT last_insert_rowid();").ToString();
                        }

                    var id = long.Parse(item.ID ?? "-1");
                    if (id <= 0)
                        throw new Exception("Invalid addition, cannot update application settings through update method");

                    SetSources(item.Sources, id, tr);
                    SetSettings(item.Settings, id, tr);
                    SetFilters(item.Filters, id, tr);
                    // Don't update the metadata if no new content is given
                    if (item.Metadata != null)
                        SetMetadata(item.Metadata, id, tr);

                    if (updateSchedule)
                    {
                        var tags = new string[] { "ID=" + item.ID };
                        var existing = GetScheduleIDsFromTags(tags);
                        if (schedule == null && existing.Any())
                            DeleteFromDb("Schedule", existing.First(), tr);
                        else if (schedule != null)
                        {
                            if (existing.Any())
                            {
                                var cur = GetSchedule(existing.First());
                                if (cur != null)
                                {
                                    cur.AllowedDays = schedule.AllowedDays;
                                    cur.Repeat = schedule.Repeat;
                                    cur.Tags = schedule.Tags;
                                    cur.Time = schedule.Time;

                                    schedule = cur;
                                }
                                else
                                {
                                    schedule.ID = -1;
                                }
                            }
                            else
                            {
                                schedule.ID = -1;
                            }

                            schedule.Tags = tags;
                            AddOrUpdateSchedule(schedule, tr);
                        }
                    }

                    tr.Commit();
                    m_notificationUpdateService?.IncrementLastDataUpdateId();
                    m_eventPollNotifyer?.SignalNewEvent();
                }
            }
        }

        internal void AddOrUpdateSchedule(ISchedule item)
        {
            lock (m_lock)
                using (var tr = m_connection.BeginTransaction())
                {
                    AddOrUpdateSchedule(item, tr);
                    tr.Commit();
                    m_notificationUpdateService?.IncrementLastDataUpdateId();
                    m_eventPollNotifyer?.SignalNewEvent();
                }
        }

        private void AddOrUpdateSchedule(ISchedule item, IDbTransaction tr)
        {
            lock (m_lock)
            {
                bool update = item.ID >= 0;
                OverwriteAndUpdateDb(
                    tr,
                    null,
                    [item],
                    cmd =>
                    {
                        if (update)
                            cmd.SetCommandAndParameters(@"UPDATE ""Schedule"" SET ""Tags""=@Tags, ""Time""=@Time, ""Repeat""=@Repeat, ""LastRun""=@LastRun, ""Rule""=@Rule WHERE ""ID""=@Id");
                        else
                            cmd.SetCommandAndParameters(@"INSERT INTO ""Schedule"" (""Tags"", ""Time"", ""Repeat"", ""LastRun"", ""Rule"") VALUES (@Tags,@Time,@Repeat,@LastRun,@Rule)");
                    },
                    (cmd, n) =>
                    {
                        cmd.SetParameterValue("@Tags", string.Join(",", n.Tags ?? new string[0]))
                            .SetParameterValue("@Time", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(n.Time))
                            .SetParameterValue("@Repeat", n.Repeat)
                            .SetParameterValue("@LastRun", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(n.LastRun))
                            .SetParameterValue("@Rule", n.Rule ?? "");

                        if (update)
                            cmd.SetParameterValue("@Id", item.ID);
                    });

                if (!update)
                    using (var cmd = m_connection.CreateCommand(tr))
                        item.ID = cmd.ExecuteScalarInt64(@"SELECT last_insert_rowid();");
            }
        }

        public void DeleteBackup(long ID)
        {
            if (ID < 0)
                return;

            lock (m_lock)
            {
                using (var tr = m_connection.BeginTransaction())
                {
                    var existing = GetScheduleIDsFromTags(new string[] { "ID=" + ID.ToString() });
                    if (existing.Any())
                        DeleteFromDb("Schedule", existing.First(), tr);

                    DeleteFromDb("ErrorLog", ID, "BackupID", tr);
                    DeleteFromDb("Filter", ID, "BackupID", tr);
                    DeleteFromDb("Log", ID, "BackupID", tr);
                    DeleteFromDb("Metadata", ID, "BackupID", tr);
                    DeleteFromDb("Option", ID, "BackupID", tr);
                    DeleteFromDb("Source", ID, "BackupID", tr);

                    DeleteFromDb("Backup", ID, tr);

                    tr.Commit();
                }
            }

            m_notificationUpdateService?.IncrementLastDataUpdateId();
            m_eventPollNotifyer?.SignalNewEvent();
        }

        public void DeleteBackup(IBackup backup)
        {
            if (backup.IsTemporary)
                UnregisterTemporaryBackup(backup);
            else
                DeleteBackup(long.Parse(backup.ID));
        }

        public void DeleteSchedule(long ID)
        {
            if (ID < 0)
                return;

            lock (m_lock)
                DeleteFromDb("Schedule", ID);

            m_notificationUpdateService?.IncrementLastDataUpdateId();
            m_eventPollNotifyer?.SignalNewEvent();
        }

        public void DeleteSchedule(ISchedule schedule)
        {
            DeleteSchedule(schedule.ID);
        }

        public IBackup[] Backups
        {
            get
            {
                lock (m_lock)
                {
                    var lst = ReadFromDb(
                        (rd) => (IBackup)new Backup()
                        {
                            ID = ConvertToInt64(rd, 0).ToString(),
                            Name = ConvertToString(rd, 1),
                            Description = ConvertToString(rd, 2),
                            Tags = (ConvertToString(rd, 3) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                            TargetURL = EncryptedFieldHelper.Decrypt(ConvertToString(rd, 4), m_key),
                            DBPath = ConvertToString(rd, 5),
                        },
                        cmd => cmd.SetCommandAndParameters(@"SELECT ""ID"", ""Name"", ""Description"", ""Tags"", ""TargetURL"", ""DBPath"" FROM ""Backup"" "))
                        .ToArray();

                    foreach (var n in lst)
                        n.Metadata = GetMetadata(long.Parse(n.ID));


                    return lst;
                }
            }
        }

        public ISchedule[] Schedules
        {
            get
            {
                lock (m_lock)
                    return ReadFromDb(
                        (rd) => (ISchedule)new Schedule()
                        {
                            ID = ConvertToInt64(rd, 0),
                            Tags = (ConvertToString(rd, 1) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                            Time = ConvertToDateTime(rd, 2),
                            Repeat = ConvertToString(rd, 3),
                            LastRun = ConvertToDateTime(rd, 4),
                            Rule = ConvertToString(rd, 5),
                        },
                        cmd => cmd.SetCommandAndParameters(@"SELECT ""ID"", ""Tags"", ""Time"", ""Repeat"", ""LastRun"", ""Rule"" FROM ""Schedule"" "))
                        .ToArray();
            }
        }


        public IFilter[] Filters
        {
            get { return GetFilters(ANY_BACKUP_ID); }
            set { SetFilters(value, ANY_BACKUP_ID); }
        }

        public ISetting[] Settings
        {
            get { return GetSettings(ANY_BACKUP_ID); }
            set { SetSettings(value, ANY_BACKUP_ID); }
        }

        public INotification[] GetNotifications()
        {
            lock (m_lock)
                return ReadFromDb<Notification>(null).Cast<INotification>().ToArray();
        }

        public bool DismissNotification(long id)
        {
            lock (m_lock)
            {
                var notifications = GetNotifications();
                var cur = notifications.FirstOrDefault(x => x.ID == id);
                if (cur == null)
                    return false;

                DeleteFromDb(typeof(Notification).Name, id);
                this.ApplicationSettings.UnackedError = notifications.Any(x => x.ID != id && x.Type == Duplicati.Server.Serialization.NotificationType.Error);
                this.ApplicationSettings.UnackedWarning = notifications.Any(x => x.ID != id && x.Type == Duplicati.Server.Serialization.NotificationType.Warning);
            }

            m_notificationUpdateService?.IncrementLastNotificationUpdateId();
            m_eventPollNotifyer?.SignalNewEvent();

            return true;
        }

        public void RegisterNotification(
            Serialization.NotificationType type,
            string title,
            string message,
            Exception? ex,
            string? backupid,
            string action,
            string? logid,
            string? messageid,
            string? logtag,
            Func<INotification, INotification[], INotification> conflicthandler)
        {
            lock (m_lock)
            {
                var notification = new Notification()
                {
                    ID = -1,
                    Type = type,
                    Title = title,
                    Message = message,
                    Exception = ex == null ? "" : ex.ToString(),
                    BackupID = backupid,
                    Action = action ?? "",
                    Timestamp = DateTime.UtcNow,
                    LogEntryID = logid,
                    MessageID = messageid,
                    MessageLogTag = logtag
                };

                var conflictResult = conflicthandler(notification, GetNotifications());
                if (conflictResult == null)
                    return;

                if (conflictResult != notification)
                    DeleteFromDb(typeof(Notification).Name, conflictResult.ID);

                OverwriteAndUpdateDb(null, null, [notification], false);

                if (type == Serialization.NotificationType.Error)
                    ApplicationSettings.UnackedError = true;
                else if (type == Serialization.NotificationType.Warning)
                    ApplicationSettings.UnackedWarning = true;
            }

            m_notificationUpdateService?.IncrementLastNotificationUpdateId();
            m_eventPollNotifyer?.SignalNewEvent();
        }

        //Workaround to clean up the database after invalid settings update
        public void FixInvalidBackupId()
        {
            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            {
                cmd.SetCommandAndParameters(@"DELETE FROM ""Option"" WHERE ""BackupID"" = @BackupId")
                    .SetParameterValue("@BackupId", -1)
                    .ExecuteNonQuery();
                cmd.SetCommandAndParameters(@"DELETE FROM ""Metadata"" WHERE ""BackupID"" = @BackupId")
                    .SetParameterValue("@BackupId", -1)
                    .ExecuteNonQuery();
                cmd.SetCommandAndParameters(@"DELETE FROM ""Filter"" WHERE ""BackupID"" = @BackupId")
                    .SetParameterValue("@BackupId", -1)
                    .ExecuteNonQuery();
                cmd.SetCommandAndParameters(@"DELETE FROM ""Source"" WHERE ""BackupID"" = @BackupId")
                    .SetParameterValue("@BackupId", -1)
                    .ExecuteNonQuery();

                cmd.SetCommandAndParameters(@"DELETE FROM ""Schedule"" WHERE ""Tags"" = @Tag")
                    .SetParameterValue("@Tag", "ID=-1")
                    .ExecuteNonQuery();
                tr.Commit();
            }

            ApplicationSettings.FixedInvalidBackupId = true;
        }

        public string[] GetUISettingsSchemes()
        {
            lock (m_lock)
                return ReadFromDb(
                    (rd) => ConvertToString(rd, 0) ?? "",
                    cmd => cmd.SetCommandAndParameters(@"SELECT DISTINCT ""Scheme"" FROM ""UIStorage"""))
                .ToArray();
        }

        public IDictionary<string, string> GetUISettings(string scheme)
        {
            lock (m_lock)
                return ReadFromDb(
                    (rd) => new KeyValuePair<string, string>(
                        ConvertToString(rd, 0) ?? "",
                        ConvertToString(rd, 1) ?? ""
                    ),
                    cmd => cmd.SetCommandAndParameters(@"SELECT ""Key"", ""Value"" FROM ""UIStorage"" WHERE ""Scheme"" = @Scheme")
                        .SetParameterValue("@Scheme", scheme))
                    .GroupBy(x => x.Key)
                    .ToDictionary(x => x.Key, x => x.Last().Value);
        }

        public void SetUISettings(string scheme, IDictionary<string, string?> values, IDbTransaction? transaction = null)
        {
            lock (m_lock)
                using (var tr = transaction == null ? m_connection.BeginTransaction() : null)
                {
                    OverwriteAndUpdateDb(
                        tr,
                        cmd => cmd.SetCommandAndParameters(@"DELETE FROM ""UIStorage"" WHERE ""Scheme"" = @Scheme")
                            .SetParameterValue("@Scheme", scheme),
                        values,
                        cmd => cmd.SetCommandAndParameters(@"INSERT INTO ""UIStorage"" (""Scheme"", ""Key"", ""Value"") VALUES (@Scheme, @Key, @Value)"),
                        (cmd, f) =>
                        {
                            cmd.SetParameterValue("@Scheme", scheme)
                                .SetParameterValue("@Key", f.Key ?? "")
                                .SetParameterValue("@Value", f.Value ?? "");
                        }
                    );

                    if (tr != null)
                        tr.Commit();
                }
        }

        public void UpdateUISettings(string scheme, IDictionary<string, string?> values, IDbTransaction? transaction = null)
        {
            lock (m_lock)
                using (var tr = transaction == null ? m_connection.BeginTransaction() : null)
                {
                    OverwriteAndUpdateDb(
                        tr,
                        cmd => cmd.SetCommandAndParameters(@"DELETE FROM ""UIStorage"" WHERE ""Scheme"" = @Scheme AND ""Key"" IN (@Keys)")
                            .SetParameterValue("@Scheme", scheme)
                            .ExpandInClauseParameter("@Keys", values.Keys),
                        values.Where(x => x.Value != null),
                        cmd => cmd.SetCommandAndParameters(@"INSERT INTO ""UIStorage"" (""Scheme"", ""Key"", ""Value"") VALUES (@Scheme, @Key, @Value)"),
                        (cmd, f) =>
                        {
                            cmd.SetParameterValue("@Scheme", scheme)
                                .SetParameterValue("@Key", f.Key ?? "")
                                .SetParameterValue("@Value", f.Value ?? "");
                        }
                    );

                    if (tr != null)
                        tr.Commit();
                }
        }

        public TempFile[] GetTempFiles()
        {
            lock (m_lock)
                return ReadFromDb<TempFile>(null).ToArray();
        }

        public void DeleteTempFile(long id)
        {
            lock (m_lock)
                DeleteFromDb(typeof(TempFile).Name, id);
        }

        public long RegisterTempFile(string origin, string path, DateTime expires)
        {
            var tempfile = new TempFile()
            {
                Timestamp = DateTime.Now,
                Origin = origin,
                Path = path,
                Expires = expires
            };

            OverwriteAndUpdateDb(null, null, [tempfile], false);

            return tempfile.ID;
        }

        public void PurgeLogData(DateTime purgeDate)
        {
            var t = Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(purgeDate);

            using (var tr = m_connection.BeginTransaction())
            using (var cmd = m_connection.CreateCommand(tr))
            {
                cmd.SetCommandAndParameters(@"DELETE FROM ""ErrorLog"" WHERE ""Timestamp"" < @Time")
                    .SetParameterValue("@Time", t)
                    .ExecuteNonQuery();

                tr.Commit();
            }
        }

        private static DateTime ConvertToDateTime(IDataReader rd, int index)
        {
            var unixTime = ConvertToInt64(rd, index);
            return unixTime == 0 ? new DateTime(0) : Library.Utility.Utility.EPOCH.AddSeconds(unixTime);
        }

        private static bool ConvertToBoolean(IDataReader rd, int index)
        {
            return ConvertToInt64(rd, index) == 1;
        }

        private static string? ConvertToString(IDataReader rd, int index)
        {
            var r = rd.GetValue(index);
            return r == null || r == DBNull.Value ? null : r.ToString();
        }

        private static long ConvertToInt64(IDataReader rd, int index)
        {
            try
            {
                if (!rd.IsDBNull(index))
                    return rd.GetInt64(index);
            }
            catch
            {
            }

            return -1;
        }

        private static long ExecuteScalarInt64(IDbCommand cmd, long defaultValue = -1)
        {
            using (var rd = cmd.ExecuteReader())
                return rd.Read() ? ConvertToInt64(rd, 0) : defaultValue;
        }

        private static string? ExecuteScalarString(IDbCommand cmd)
        {
            using (var rd = cmd.ExecuteReader())
                return rd.Read() ? ConvertToString(rd, 0) : null;

        }

        private object? ConvertToEnum(Type enumType, IDataReader rd, int index, object? @default)
        {
            try
            {
                return Enum.Parse(enumType, ConvertToString(rd, index) ?? string.Empty, true);
            }
            catch
            {
            }

            return @default;
        }

        // Overloaded function for legacy functionality
        private bool DeleteFromDb(string tablename, long id, IDbTransaction? transaction = null)
        {
            return DeleteFromDb(tablename, id, "ID", transaction);
        }

        // New function that allows to delete rows from tables with arbitrary identifier values (e.g. ID or BackupID)
        private bool DeleteFromDb(string tablename, long id, string identifier, IDbTransaction? transaction = null)
        {
            if (transaction == null)
            {
                using (var tr = m_connection.BeginTransaction())
                {
                    var r = DeleteFromDb(tablename, id, tr);
                    tr.Commit();
                    return r;
                }
            }
            else
            {
                using (var cmd = m_connection.CreateCommand(transaction))
                {
                    cmd.SetCommandAndParameters(string.Format(CultureInfo.InvariantCulture, @"DELETE FROM ""{0}"" WHERE ""{1}""=@Value", tablename, identifier))
                        .SetParameterValue("@Value", id);

                    var r = cmd.ExecuteNonQuery();
                    // Roll back the transaction if more than 1 ID was deleted. Multiple "BackupID" rows being deleted isn't a problem.
                    if (identifier == "ID" && r > 1)
                        throw new Exception(string.Format("Too many records attempted deleted from table {0} for id {1}: {2}", tablename, id, r));
                    return r == 1;
                }
            }
        }

        private static IEnumerable<T> Read<T>(IDbCommand cmd, Func<IDataReader, T> f)
        {
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    yield return f(rd);
        }

        private static IEnumerable<T> Read<T>(IDataReader rd, Func<T> f)
        {
            while (rd.Read())
                yield return f();
        }

        private System.Reflection.PropertyInfo[] GetORMFields<T>()
        {
            var flags =
                System.Reflection.BindingFlags.FlattenHierarchy |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public;

            var supportedPropertyTypes = new Type[] {
                typeof(long),
                typeof(string),
                typeof(bool),
                typeof(DateTime)
            };

            return
                (from n in typeof(T).GetProperties(flags)
                 where supportedPropertyTypes.Contains(n.PropertyType) || n.PropertyType.IsEnum
                 select n).ToArray();
        }

        private IEnumerable<T> ReadFromDb<T>(Action<IDbCommand>? prep)
        {
            var properties = GetORMFields<T>();

            var sql = string.Format(CultureInfo.InvariantCulture,
                @"SELECT ""{0}"" FROM ""{1}""",
                string.Join(@""", """, properties.Select(x => x.Name)),
                typeof(T).Name
            );

            return ReadFromDb((rd) =>
            {
                var item = Activator.CreateInstance<T>();
                for (var i = 0; i < properties.Length; i++)
                {
                    var prop = properties[i];

                    if (prop.PropertyType.IsEnum)
                        prop.SetValue(item, ConvertToEnum(prop.PropertyType, rd, i, Enum.GetValues(prop.PropertyType).GetValue(0)), null);
                    else if (prop.PropertyType == typeof(string))
                        prop.SetValue(item, ConvertToString(rd, i), null);
                    else if (prop.PropertyType == typeof(long))
                        prop.SetValue(item, ConvertToInt64(rd, i), null);
                    else if (prop.PropertyType == typeof(bool))
                        prop.SetValue(item, ConvertToBoolean(rd, i), null);
                    else if (prop.PropertyType == typeof(DateTime))
                        prop.SetValue(item, ConvertToDateTime(rd, i), null);
                }

                return item;
            },
            cmd =>
            {
                cmd.SetCommandAndParameters(sql);
                if (prep != null)
                    prep(cmd);
            });
        }

        private void OverwriteAndUpdateDb<T>(IDbTransaction? transaction, Action<IDbCommand>? deletePrep, IEnumerable<T> values, bool updateExisting)
        {
            var properties = GetORMFields<T>();
            var idfield = properties.FirstOrDefault(x => x.Name == "ID")
                ?? throw new Exception("No ID field found in type " + typeof(T).Name);

            var nonIdProps = properties.Where(x => x.Name != "ID").ToArray();

            string sql;

            if (updateExisting)
            {
                sql = string.Format(
                    @"UPDATE ""{0}"" SET {1} WHERE ""ID""= @Id",
                    typeof(T).Name,
                    string.Join(@", ", nonIdProps.Select(x => @$"""{x.Name}"" = @{x.Name}"))
                );

                properties = properties.Append(idfield).ToArray();
            }
            else
            {

                sql = string.Format(
                    @"INSERT INTO ""{0}"" (""{1}"") VALUES ({2})",
                    typeof(T).Name,
                    string.Join(@""", """, nonIdProps.Select(x => x.Name)),
                    string.Join(@", ", nonIdProps.Select(x => $"@{x.Name}"))
                );
            }

            OverwriteAndUpdateDb(transaction, deletePrep, values,
                cmd => cmd.SetCommandAndParameters(sql),
                (cmd, item) =>
                {
                    foreach (var p in properties)
                    {
                        if (!updateExisting && p == idfield)
                            continue;

                        var val = p.GetValue(item, null);
                        if (val != null)
                        {
                            if (p.PropertyType.IsEnum)
                                val = val.ToString();
                            else if (p.PropertyType == typeof(DateTime))
                                val = Library.Utility.Utility.NormalizeDateTimeToEpochSeconds((DateTime)val);
                        }

                        cmd.SetParameterValue($"@{p.Name}", val);
                    }
                });

            if (!updateExisting && values.Count() == 1 && idfield != null)
                using (var cmd = m_connection.CreateCommand(transaction))
                {
                    cmd.SetCommandAndParameters(@"SELECT last_insert_rowid();");
                    if (idfield.PropertyType == typeof(string))
                        idfield.SetValue(values.First(), ExecuteScalarString(cmd), null);
                    else
                        idfield.SetValue(values.First(), ExecuteScalarInt64(cmd), null);
                }
        }

        private IEnumerable<T> ReadFromDb<T>(Func<IDataReader, T> f, Action<IDbCommand>? prep)
        {
            using (var cmd = m_connection.CreateCommand())
            {
                if (prep != null)
                    prep(cmd);
                return Read(cmd, f).ToArray();
            }
        }

        private void OverwriteAndUpdateDb<T>(IDbTransaction? transaction, Action<IDbCommand>? deletePrep, IEnumerable<T> values, Action<IDbCommand> insertPrep, Action<IDbCommand, T> insert)
        {
            using (var cmd = m_connection.CreateCommand(transaction))
            {
                if (deletePrep != null)
                {
                    deletePrep(cmd);
                    cmd.ExecuteNonQuery();
                }

                insertPrep(cmd);
                foreach (var v in values)
                {
                    insert(cmd, v);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Encrypts sensitive fields
        /// </summary>
        /// <param name="fieldName">The fieldname used to determine if it will be encrypted</param>
        /// <param name="fieldValue">The field value</param>
        /// <param name="key">The encryption key</param>
        /// <returns>The encrypted string or the original value</returns>
        private static string? EncryptSensitiveFields(string fieldName, string fieldValue, EncryptedFieldHelper.KeyInstance? key)
        {
            if (fieldValue != null)
                return _encryptedFields.Contains(fieldName)
                    ? EncryptedFieldHelper.Encrypt(fieldValue, key)
                    : fieldValue;

            return null;
        }

        /// <summary>
        /// Decrypts sensitive fields
        /// </summary>
        /// <param name="fieldValue">The field value</param>
        /// <param name="key">The encryption key</param>
        /// <returns>The decrypted string</returns>
        private static string? DecryptSensitiveFields(string? fieldValue, EncryptedFieldHelper.KeyInstance? key)
        {
            if (fieldValue != null)
                return EncryptedFieldHelper.IsEncryptedString(fieldValue)
                    ? EncryptedFieldHelper.Decrypt(fieldValue, key)
                    : fieldValue;

            return null;
        }

        #region IDisposable implementation
        public void Dispose()
        {
            try { m_errorcmd?.Dispose(); }
            catch { }

            try { m_connection?.Dispose(); }
            catch { }
        }
        #endregion
    }

}

