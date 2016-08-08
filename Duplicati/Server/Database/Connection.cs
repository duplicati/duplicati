//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Server.Serialization.Interface;
using System.Text;

namespace Duplicati.Server.Database
{
    public class Connection : IDisposable
    {
        private System.Data.IDbConnection m_connection;
        private System.Data.IDbCommand m_errorcmd;
        public readonly object m_lock = new object();
        public const int ANY_BACKUP_ID = -1;
        public const int APP_SETTINGS_ID = -2;
        private Dictionary<string, Backup> m_temporaryBackups = new Dictionary<string, Backup>();

        public Connection(System.Data.IDbConnection connection)
        {
            m_connection = connection;
            m_errorcmd = m_connection.CreateCommand();
            m_errorcmd.CommandText = @"INSERT INTO ""ErrorLog"" (""BackupID"", ""Message"", ""Exception"", ""Timestamp"") VALUES (?,?,?,?)";
            for(var i = 0; i < 4; i++)
                m_errorcmd.Parameters.Add(m_errorcmd.CreateParameter());
            
            this.ApplicationSettings = new ApplicationSettings(this);
        }
                
        internal void LogError(string backupid, string message, Exception ex)
        {
            lock(m_lock)
            {
                long id;
                if (!long.TryParse(backupid, out id))
                    id = -1;
                ((System.Data.IDbDataParameter)m_errorcmd.Parameters[0]).Value = id;
                ((System.Data.IDbDataParameter)m_errorcmd.Parameters[1]).Value = message;
                ((System.Data.IDbDataParameter)m_errorcmd.Parameters[2]).Value = ex == null ? null : ex.ToString();
                ((System.Data.IDbDataParameter)m_errorcmd.Parameters[3]).Value = NormalizeDateTimeToEpochSeconds(DateTime.UtcNow);
                m_errorcmd.ExecuteNonQuery();
            }
        }
        
        internal void ExecuteWithCommand(Action<System.Data.IDbCommand> f)
        {
            lock(m_lock)
                using(var cmd = m_connection.CreateCommand())
                    f(cmd);
        }

        internal Serializable.ImportExportStructure PrepareBackupForExport(IBackup backup)
        {
            var scheduleId = GetScheduleIDsFromTags(new string[] { "ID=" + backup.ID });
            return new Serializable.ImportExportStructure() {
                    CreatedByVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    Backup = (Database.Backup)backup,
                    Schedule = (Database.Schedule)(scheduleId.Any() ? GetSchedule(scheduleId.First()) : null),
                    DisplayNames = SpecialFolders.GetSourceNames(backup)
                };
        }
                
        public string RegisterTemporaryBackup(IBackup backup)
        {
            lock(m_lock)
            {
                if (backup == null)
                    throw new ArgumentNullException("backup");
                if (backup.ID != null)
                    throw new ArgumentException("Backup is already active, cannot make temporary");
                
                backup.ID = Guid.NewGuid().ToString("D");
                m_temporaryBackups.Add(backup.ID, (Backup)backup);
                return backup.ID;
            }
        }
        
        public void UnregisterTemporaryBackup(IBackup backup)
        {
            lock(m_lock)
                m_temporaryBackups.Remove(backup.ID);
        }

        public void UpdateTemporaryBackup(IBackup backup)
        {
            lock(m_lock)
                if (m_temporaryBackups.Remove(backup.ID))
                    m_temporaryBackups.Add(backup.ID, (Backup)backup);
        }

        public IBackup GetTemporaryBackup(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            
            lock(m_lock)
            {
                Backup b;
                m_temporaryBackups.TryGetValue(id, out b);
                return b;
            }
        }

        public ApplicationSettings ApplicationSettings { get; private set; }
        
        internal IDictionary<string, string> GetMetadata(long id)
        {
            lock(m_lock)
                return ReadFromDb(
                    (rd) => new KeyValuePair<string, string>(
                        ConvertToString(rd, 0),
                        ConvertToString(rd, 1)
                    ),
                    @"SELECT ""Name"", ""Value"" FROM ""Metadata"" WHERE ""BackupID"" = ? ", id)
                    .ToDictionary((k) => k.Key, (k) => k.Value);
        }
        
        internal void SetMetadata(IDictionary<string, string> values, long id, System.Data.IDbTransaction transaction)
        {
            lock(m_lock)
                using(var tr = transaction == null ? m_connection.BeginTransaction() : null)
                {
                    OverwriteAndUpdateDb(
                        tr,
                        @"DELETE FROM ""Metadata"" WHERE ""BackupID"" = ?", new object[] { id },
                        values ?? new Dictionary<string, string>(),
                        @"INSERT INTO ""Metadata"" (""BackupID"", ""Name"", ""Value"") VALUES (?, ?, ?)",
                        (f) => new object[] { id, f.Key, f.Value }
                    );
                    
                    if (tr != null)
                        tr.Commit();
                }
        }
        
        internal IFilter[] GetFilters(long id)
        {
            lock(m_lock)
                return ReadFromDb(
                    (rd) => (IFilter)new Filter() {
                    Order = ConvertToInt64(rd, 0),
                        Include = ConvertToBoolean(rd, 1),
                        Expression = ConvertToString(rd, 2) ?? ""
                    },
                    @"SELECT ""Order"", ""Include"", ""Expression"" FROM ""Filter"" WHERE ""BackupID"" = ? ORDER BY ""Order"" ", id)
                    .ToArray();
        }
        
        internal void SetFilters(IEnumerable<IFilter> values, long id, System.Data.IDbTransaction transaction = null)
        {
            lock(m_lock)
                using(var tr = transaction == null ? m_connection.BeginTransaction() : null)
                {
                    OverwriteAndUpdateDb(
                        tr,
                        @"DELETE FROM ""Filter"" WHERE ""BackupID"" = ?", new object[] { id },
                        values,
                        @"INSERT INTO ""Filter"" (""BackupID"", ""Order"", ""Include"", ""Expression"") VALUES (?, ?, ?, ?)",
                        (f) => new object[] { id, f.Order, f.Include, f.Expression }
                    );
                    
                    if (tr != null)
                        tr.Commit();
                }
        }
        
        internal ISetting[] GetSettings(long id)
        {
            lock(m_lock)
                return ReadFromDb(
                    (rd) => (ISetting)new Setting() {
                        Filter = ConvertToString(rd, 0) ?? "",
                        Name = ConvertToString(rd, 1) ?? "",
                        Value = ConvertToString(rd, 2) ?? ""
                        //TODO: Attach the argument information
                    },
                    @"SELECT ""Filter"", ""Name"", ""Value"" FROM ""Option"" WHERE ""BackupID"" = ?", id)
                    .ToArray();
        }
        
        internal void SetSettings(IEnumerable<ISetting> values, long id, System.Data.IDbTransaction transaction = null)
        {
            lock(m_lock)
                using(var tr = transaction == null ? m_connection.BeginTransaction() : null)
                {
                    OverwriteAndUpdateDb(
                        tr,
                        @"DELETE FROM ""Option"" WHERE ""BackupID"" = ?", new object[] { id },
                        values,
                        @"INSERT INTO ""Option"" (""BackupID"", ""Filter"", ""Name"", ""Value"") VALUES (?, ?, ?, ?)",
                        (f) => {
                            if (Duplicati.Server.WebServer.Server.PASSWORD_PLACEHOLDER.Equals(f.Value))
                                throw new Exception("Attempted to save a property with the placeholder password");
                            return new object[] { id, f.Filter ?? "", f.Name, f.Value ?? "" };
                        }
                    );            
                    
                    if (tr != null)
                        tr.Commit();
                }
        }
        
        internal string[] GetSources(long id)
        {
            lock(m_lock)
                return ReadFromDb(
                    (rd) => ConvertToString(rd, 0),
                    @"SELECT ""Path"" FROM ""Source"" WHERE ""BackupID"" = ?", id)
                    .ToArray();
        }
        
        internal void SetSources(IEnumerable<string> values, long id, System.Data.IDbTransaction transaction)
        {
            lock(m_lock)
                using(var tr = transaction == null ? m_connection.BeginTransaction() : null)
                {
                    OverwriteAndUpdateDb(
                        tr,
                        @"DELETE FROM ""Source"" WHERE ""BackupID"" = ?", new object[] { id },
                        values,
                        @"INSERT INTO ""Source"" (""BackupID"", ""Path"") VALUES (?, ?)",
                        (f) => new object[] { id, f }
                    );            
                    
                    if (tr != null)
                        tr.Commit();
                }
        }

        internal long[] GetBackupIDsForTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
                return new long[0];
                
            if (tags.Length == 1 && tags[0].StartsWith("ID="))
                return new long[] { long.Parse(tags[0].Substring("ID=".Length)) };
                
            lock(m_lock)
                using(var cmd = m_connection.CreateCommand())
                {
                    var sb = new StringBuilder();
                    
                    foreach(var t in tags)
                    {
                        if (sb.Length != 0)
                            sb.Append(" OR ");
                        sb.Append(@" ("","" || ""Tags"" || "","" LIKE ""%,"" || ? || "",%"") ");
                        
                        var p = cmd.CreateParameter();
                        p.Value = t;
                        cmd.Parameters.Add(p);
                    }
                
                    cmd.CommandText = @"SELECT ""ID"" FROM ""Backup"" WHERE " + sb.ToString();
                    
                    return Read(cmd, (rd) => ConvertToInt64(rd, 0)).ToArray();
                }
        }

        internal IBackup GetBackup(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException("id");
            
            long lid;
            if (long.TryParse(id, out lid))
                return GetBackup(lid);
            else
                return GetTemporaryBackup(id);
        }

        internal IBackup GetBackup(long id)
        {
            lock(m_lock)
            {
                var bk = ReadFromDb(
                    (rd) => new Backup() {
                        ID = ConvertToInt64(rd, 0).ToString(),
                        Name = ConvertToString(rd, 1),
                        Tags = (ConvertToString(rd, 2) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                        TargetURL = ConvertToString(rd, 3),
                        DBPath = ConvertToString(rd, 4),
                    },
                    @"SELECT ""ID"", ""Name"", ""Tags"", ""TargetURL"", ""DBPath"" FROM ""Backup"" WHERE ID = ?", id)
                    .FirstOrDefault();
                    
                if (bk != null)
                    bk.LoadChildren(this);
                    
                return bk;
            }
        }
        
        internal ISchedule GetSchedule(long id)
        {
            lock(m_lock)
            {
                var bk = ReadFromDb(
                    (rd) => new Schedule() {
                        ID = ConvertToInt64(rd, 0),
                        Tags = (ConvertToString(rd, 1) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                        Time = ConvertToDateTime(rd, 2),
                        Repeat = ConvertToString(rd, 3),
                        LastRun = ConvertToDateTime(rd, 4),
                        Rule = ConvertToString(rd, 5),
                    },
                    @"SELECT ""ID"", ""Tags"", ""Time"", ""Repeat"", ""LastRun"", ""Rule"" FROM ""Schedule"" WHERE ID = ?", id)
                    .FirstOrDefault();

                return bk;
            }
        }        

        internal long[] GetScheduleIDsFromTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
                return new long[0];
                                
            lock(m_lock)
                using(var cmd = m_connection.CreateCommand())
                {
                    var sb = new StringBuilder();
                    
                    foreach(var t in tags)
                    {
                        if (sb.Length != 0)
                            sb.Append(" OR ");
                        sb.Append(@" ("","" || ""Tags"" || "","" LIKE ""%,"" || ? || "",%"") ");
                        
                        var p = cmd.CreateParameter();
                        p.Value = t;
                        cmd.Parameters.Add(p);
                    }
                
                    cmd.CommandText = @"SELECT ""ID"" FROM ""Schedule"" WHERE " + sb.ToString();
                    
                    return Read(cmd, (rd) => ConvertToInt64(rd, 0)).ToArray();
                }
        }

        internal void AddOrUpdateBackup(IBackup item)
        {
            AddOrUpdateBackup(item, false, null);
        }
        
        internal void AddOrUpdateBackupAndSchedule(IBackup item, ISchedule schedule)
        {
            AddOrUpdateBackup(item, true, schedule);
        }

        internal void UpdateBackupDBPath(IBackup item, string path)
        {
            lock(m_lock)
            using(var tr = m_connection.BeginTransaction())
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = tr;
                cmd.Parameters.Add(cmd.CreateParameter());
                ((System.Data.IDbDataParameter)cmd.Parameters[0]).Value = path;
                cmd.Parameters.Add(cmd.CreateParameter());
                ((System.Data.IDbDataParameter)cmd.Parameters[1]).Value = item.ID;

                cmd.CommandText = @"UPDATE ""Backup"" SET ""DBPath""=? WHERE ""ID""=?";
                cmd.ExecuteNonQuery();
                tr.Commit();
            }
            
            System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
            Program.StatusEventNotifyer.SignalNewEvent();
        }

        private void AddOrUpdateBackup(IBackup item, bool updateSchedule, ISchedule schedule)
        {
            lock(m_lock)
            {
                bool update = item.ID != null;
                if (!update && item.DBPath == null)
                {
                    var folder = Program.DATAFOLDER;
                    if (!System.IO.Directory.Exists(folder))
                        System.IO.Directory.CreateDirectory(folder);
                    
                    for(var i = 0; i < 100; i++)
                    {
                        var guess = System.IO.Path.Combine(folder, System.IO.Path.ChangeExtension(Duplicati.Library.Main.DatabaseLocator.GenerateRandomName(), ".sqlite"));
                        if (!System.IO.File.Exists(guess))
                        {
                            ((Backup)item).DBPath = guess;
                            break;
                        }
                    }
                    
                    if (item.DBPath == null)
                        throw new Exception("Unable to generate a unique database file name");
                }
                
                using(var tr = m_connection.BeginTransaction())
                {
                    OverwriteAndUpdateDb(
                        tr,
                        null,
                        new object[] { long.Parse(item.ID ?? "-1") },
                        new IBackup[] { item },
                        update ?
                            @"UPDATE ""Backup"" SET ""Name""=?, ""Tags""=?, ""TargetURL""=? WHERE ""ID""=?" :
                            @"INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"") VALUES (?,?,?,?)",
                        (n) => {
                        
                            if (n.TargetURL.IndexOf(Duplicati.Server.WebServer.Server.PASSWORD_PLACEHOLDER) >= 0)
                                throw new Exception("Attempted to save a backup with the password placeholder");
                            if (update && long.Parse(n.ID) <= 0)
                                throw new Exception("Invalid update, cannot update application settings through update method");
                           
                            return new object[] {
                                n.Name,
                                string.Join(",", n.Tags ?? new string[0]),
                                n.TargetURL,
                                update ? (object)item.ID : (object)n.DBPath 
                            };
                        });
                        
                    if (!update)
                        using(var cmd = m_connection.CreateCommand())
                        {
                            cmd.Transaction = tr;
                            cmd.CommandText = @"SELECT last_insert_rowid();";
                            item.ID = ExecuteScalarInt64(cmd).ToString();
                        }
                        
                    var id = long.Parse(item.ID);

                    if (long.Parse(item.ID) <= 0)
                        throw new Exception("Invalid addition, cannot update application settings through update method");

                    SetSources(item.Sources, id, tr);
                    SetSettings(item.Settings, id, tr);
                    SetFilters(item.Filters, id, tr);
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
                            
                            schedule.Tags = tags;
                            AddOrUpdateSchedule(schedule, tr);
                        }
                    }
                    
                    tr.Commit();
                    System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
                    Program.StatusEventNotifyer.SignalNewEvent();
                }
            }
        }
        
        internal void AddOrUpdateSchedule(ISchedule item)
        {
            lock(m_lock)
                using(var tr = m_connection.BeginTransaction())
                {
                    AddOrUpdateSchedule(item, tr);
                    tr.Commit();
                    System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
                    Program.StatusEventNotifyer.SignalNewEvent();
                }
        }
        
        private void AddOrUpdateSchedule(ISchedule item, System.Data.IDbTransaction tr)
        {
            lock(m_lock)
            {
                bool update = item.ID >= 0;
                OverwriteAndUpdateDb(
                    tr,
                    null,
                    new object[] { item.ID },
                    new ISchedule[] { item },
                    update ?
                        @"UPDATE ""Schedule"" SET ""Tags""=?, ""Time""=?, ""Repeat""=?, ""LastRun""=?, ""Rule""=? WHERE ""ID""=?" :
                        @"INSERT INTO ""Schedule"" (""Tags"", ""Time"", ""Repeat"", ""LastRun"", ""Rule"") VALUES (?,?,?,?,?)",
                    (n) => new object[] {
                        string.Join(",", n.Tags),
                        NormalizeDateTimeToEpochSeconds(n.Time),
                        n.Repeat,
                        NormalizeDateTimeToEpochSeconds(n.LastRun),
                        n.Rule ?? "",
                        update ? (object)item.ID : null
                    });
                    
                if (!update)
                    using(var cmd = m_connection.CreateCommand())
                    {
                        cmd.Transaction = tr;
                        cmd.CommandText = @"SELECT last_insert_rowid();";
                        item.ID = ExecuteScalarInt64(cmd);
                    }
            }
        }

        public void DeleteBackup(long ID)
        {
            if (ID < 0)
                return;
            
            lock(m_lock)
            {
                using(var tr = m_connection.BeginTransaction())
                {
                    var existing = GetScheduleIDsFromTags(new string[] { "ID=" + ID.ToString() });
                    if (existing.Any())
                        DeleteFromDb("Schedule", existing.First(), tr);
                    
                    DeleteFromDb("Backup", ID, tr);
                    
                    tr.Commit();
                }
            }
            
            System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
            Program.StatusEventNotifyer.SignalNewEvent();
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
            
            lock(m_lock)
                DeleteFromDb("Schedule", ID);
            
            System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
            Program.StatusEventNotifyer.SignalNewEvent();
        }
        
        public void DeleteSchedule(ISchedule schedule)
        {
            DeleteSchedule(schedule.ID);
        }
        
        public IBackup[] Backups
        {
            get
            {
                lock(m_lock)
                {
                    var lst = ReadFromDb(
                        (rd) => (IBackup)new Backup() {
                            ID = ConvertToInt64(rd, 0).ToString(),
                            Name = ConvertToString(rd, 1),
                            Tags = (ConvertToString(rd, 2) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                            TargetURL = ConvertToString(rd, 3),
                            DBPath = ConvertToString(rd, 4),
                        },
                        @"SELECT ""ID"", ""Name"", ""Tags"", ""TargetURL"", ""DBPath"" FROM ""Backup"" ")
                        .ToArray();
                        
                    foreach(var n in lst)
                        n.Metadata = GetMetadata(long.Parse(n.ID));
                        
                    
                    return lst;
                }
            }
        }
        
        public ISchedule[] Schedules
        {
            get
            {
                lock(m_lock)
                    return ReadFromDb(
                        (rd) => (ISchedule)new Schedule() {
                            ID = ConvertToInt64(rd, 0),
                            Tags = (ConvertToString(rd, 1) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                            Time = ConvertToDateTime(rd, 2),
                            Repeat = ConvertToString(rd, 3),
                            LastRun = ConvertToDateTime(rd, 4),
                            Rule = ConvertToString(rd, 5),
                        },
                        @"SELECT ""ID"", ""Tags"", ""Time"", ""Repeat"", ""LastRun"", ""Rule"" FROM ""Schedule"" ")
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
            lock(m_lock)
                return ReadFromDb<Notification>(null).Cast<INotification>().ToArray();
        }

        public bool DismissNotification(long id)
        {
            lock(m_lock)
            {
                var notifications = GetNotifications();
                var cur = notifications.Where(x => x.ID == id).FirstOrDefault();
                if (cur == null)
                    return false;

                DeleteFromDb(typeof(Notification).Name, id);
                Program.DataConnection.ApplicationSettings.UnackedError = notifications.Where(x => x.ID != id && x.Type == Duplicati.Server.Serialization.NotificationType.Error).Any();
                Program.DataConnection.ApplicationSettings.UnackedWarning = notifications.Where(x => x.ID != id && x.Type == Duplicati.Server.Serialization.NotificationType.Warning).Any();
            }

            System.Threading.Interlocked.Increment(ref Program.LastNotificationUpdateID);
            Program.StatusEventNotifyer.SignalNewEvent();

            return true;
        }

        public void RegisterNotification(Serialization.NotificationType type, string title, string message, Exception ex, string backupid, string action, Func<INotification, INotification[], INotification> conflicthandler)
        {
            lock(m_lock)
            {
                var notification = new Notification() {
                    ID = -1,
                    Type = type,
                    Title = title,
                    Message = message,
                    Exception = ex == null ? "" : ex.ToString(),
                    BackupID = backupid,
                    Action = action ?? "",
                    Timestamp = DateTime.UtcNow
                };

                var conflictResult = conflicthandler(notification, GetNotifications());
                if (conflictResult == null)
                    return;
                
                if (conflictResult != notification)
                    DeleteFromDb(typeof(Notification).Name, conflictResult.ID);

                OverwriteAndUpdateDb(null, null, null, new Notification[] { notification }, false);

                if (type == Duplicati.Server.Serialization.NotificationType.Error)
                    Program.DataConnection.ApplicationSettings.UnackedError = true;
                else if (type == Duplicati.Server.Serialization.NotificationType.Warning)
                    Program.DataConnection.ApplicationSettings.UnackedWarning = true;
            }

            System.Threading.Interlocked.Increment(ref Program.LastNotificationUpdateID);
            Program.StatusEventNotifyer.SignalNewEvent();
        }

        //Workaround to clean up the database after invalid settings update
        public void FixInvalidBackupId()
        {
            using(var cmd = m_connection.CreateCommand())
            using (var tr = m_connection.BeginTransaction())
            {
                cmd.Transaction = tr;
                cmd.Parameters.Add(cmd.CreateParameter());
                ((System.Data.IDbDataParameter)cmd.Parameters[0]).Value = -1;
                cmd.CommandText = @"DELETE FROM ""Option"" WHERE ""BackupID"" = ?";
                cmd.ExecuteNonQuery();
                cmd.CommandText = @"DELETE FROM ""Metadata"" WHERE ""BackupID"" = ?";
                cmd.ExecuteNonQuery();
                cmd.CommandText = @"DELETE FROM ""Filter"" WHERE ""BackupID"" = ?";
                cmd.ExecuteNonQuery();
                cmd.CommandText = @"DELETE FROM ""Source"" WHERE ""BackupID"" = ?";
                cmd.ExecuteNonQuery();

                cmd.Parameters.Clear();
                cmd.Parameters.Add(cmd.CreateParameter());

                ((System.Data.IDbDataParameter)cmd.Parameters[0]).Value = "ID=-1";
                cmd.CommandText = @"DELETE FROM ""Schedule"" WHERE ""Tags"" = ?";
                cmd.ExecuteNonQuery();
                tr.Commit();
            }

            ApplicationSettings.FixedInvalidBackupId = true;
        }

        public string[] GetUISettingsSchemes()
        {
            lock(m_lock)
                return ReadFromDb(
                    (rd) => ConvertToString(rd, 0) ?? "",
                    @"SELECT DISTINCT ""Scheme"" FROM ""UIStorage""")
                .ToArray();
        }

        public IDictionary<string, string> GetUISettings(string scheme)
        {
            lock(m_lock)
                return ReadFromDb(
                    (rd) => new KeyValuePair<string, string>(
                        ConvertToString(rd, 0) ?? "",
                        ConvertToString(rd, 1) ?? ""
                    ),
                    @"SELECT ""Key"", ""Value"" FROM ""UIStorage"" WHERE ""Scheme"" = ?", 
                    scheme)
                    .ToDictionary(x => x.Key, x => x.Value);
        }
        
        public void SetUISettings(string scheme, IDictionary<string, string> values, System.Data.IDbTransaction transaction = null)
        {
            lock(m_lock)
                using(var tr = transaction == null ? m_connection.BeginTransaction() : null)
                {
                    OverwriteAndUpdateDb(
                        tr,
                        @"DELETE FROM ""UIStorage"" WHERE ""Scheme"" = ?", new object[] { scheme },
                        values,
                        @"INSERT INTO ""UIStorage"" (""Scheme"", ""Key"", ""Value"") VALUES (?, ?, ?)",
                        (f) => {
                            return new object[] { scheme, f.Key ?? "", f.Value ?? "" };
                        }
                    );            
                    
                    if (tr != null)
                        tr.Commit();
                }
        }

        public TempFile[] GetTempFiles()
        {
            lock(m_lock)
                return ReadFromDb<TempFile>(null).ToArray();
        }

        public void DeleteTempFile(long id)
        {
            lock(m_lock)
                DeleteFromDb(typeof(TempFile).Name, id);
        }

        public long RegisterTempFile(string origin, string path, DateTime expires)
        {
            var tempfile = new TempFile() {
                Timestamp = DateTime.Now,
                Origin = origin,
                Path = path,
                Expires = expires
            };

            OverwriteAndUpdateDb(null, null, null, new TempFile[] { tempfile }, false);

            return tempfile.ID;
        }

        /// <summary>
        /// Normalizes a DateTime instance floor'ed to seconds and in UTC
        /// </summary>
        /// <returns>The normalised date time</returns>
        /// <param name="input">The input time</param>
        public static DateTime NormalizeDateTime(DateTime input)
        {
            var ticks = input.ToUniversalTime().Ticks;
            ticks -= ticks % TimeSpan.TicksPerSecond;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        public void PurgeLogData(DateTime purgeDate)
        {
            var t = NormalizeDateTimeToEpochSeconds(purgeDate);

            using(var tr = m_connection.BeginTransaction())
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = tr;
                cmd.CommandText = @"DELETE FROM ""ErrorLog"" WHERE ""Timestamp"" < ?";
                cmd.Parameters.Add(cmd.CreateParameter());
                ((System.Data.IDataParameter)cmd.Parameters[0]).Value = t;
                cmd.ExecuteNonQuery();

                tr.Commit();
            }

            using(var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = "VACUUM";
                cmd.ExecuteNonQuery();
            }

        }
        
        private static long NormalizeDateTimeToEpochSeconds(DateTime input)
        {
            return (long)Math.Floor((NormalizeDateTime(input) - Library.Utility.Utility.EPOCH).TotalSeconds);
        }
        
        private static DateTime ConvertToDateTime(System.Data.IDataReader rd, int index)
        {
            var unixTime = ConvertToInt64(rd, index);
            if (unixTime == 0)
                return new DateTime(0);
            
            return Library.Utility.Utility.EPOCH.AddSeconds(unixTime);
        }
        
        private static bool ConvertToBoolean(System.Data.IDataReader rd, int index)
        {
            return ConvertToInt64(rd, index) == 1;
        }
        
        private static string ConvertToString(System.Data.IDataReader rd, int index)
        {
            var r = rd.GetValue(index);
            if (r == null || r == DBNull.Value)
                return null;
            else
                return r.ToString();
        }

        private static long ConvertToInt64(System.Data.IDataReader rd, int index)
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

        private static long ExecuteScalarInt64(System.Data.IDbCommand cmd, long defaultValue = -1)
        {
            using(var rd = cmd.ExecuteReader())
                if (rd.Read())
                    return ConvertToInt64(rd, 0);
                else
                    return defaultValue;
        }

        private static string ExecuteScalarString(System.Data.IDbCommand cmd)
        {
            using(var rd = cmd.ExecuteReader())
                if (rd.Read())
                    return ConvertToString(rd, 0);
                else
                    return null;

        }

        private T ConvertToEnum<T>(System.Data.IDataReader rd, int index, T @default)
            where T : struct
        {
            T res;
            if (!Enum.TryParse<T>(ConvertToString(rd, index), true, out res))
                return @default;
            return res;
        }

        private object ConvertToEnum(Type enumType, System.Data.IDataReader rd, int index, object @default)
        {
            try
            {
                return Enum.Parse(enumType, ConvertToString(rd, index));
            }
            catch
            {
            }

            return @default;
        }
                        
        private bool DeleteFromDb(string tablename, long id, System.Data.IDbTransaction transaction = null)                        
        {
            if (transaction == null) 
            {
                using(var tr = m_connection.BeginTransaction())
                {
                    var r = DeleteFromDb(tablename, id, tr);
                    tr.Commit();
                    return r;
                }
            }
            else
            {
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = string.Format(@"DELETE FROM ""{0}"" WHERE ID=?", tablename);
                    var p = cmd.CreateParameter();
                    p.Value = id;
                    cmd.Parameters.Add(p);
                    
                    var r = cmd.ExecuteNonQuery();
                    if (r > 1)
                        throw new Exception(string.Format("Too many records attempted deleted from table {0} for id {1}: {2}", tablename, id, r));
                    return r == 1;
                }
            }
        }
        
        private static IEnumerable<T> Read<T>(System.Data.IDbCommand cmd, Func<System.Data.IDataReader, T> f)
        {
            using(var rd = cmd.ExecuteReader())
                while(rd.Read())
                    yield return f(rd);
        }
        
        private static IEnumerable<T> Read<T>(System.Data.IDataReader rd, Func<T> f)
        {
            while(rd.Read())
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

        private IEnumerable<T> ReadFromDb<T>(string whereclause, params object[] args)
        {
            var properties = GetORMFields<T>();

            var sql = string.Format(
                @"SELECT ""{0}"" FROM ""{1}"" {2} {3}",
                string.Join(@""", """, properties.Select(x => x.Name)),
                typeof(T).Name,
                string.IsNullOrWhiteSpace(whereclause) ? "" : " WHERE ",
                whereclause ?? ""
            );

            return ReadFromDb((rd) => {
                    var item = Activator.CreateInstance<T>();
                    for(var i = 0; i < properties.Length; i++)
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
                }, sql, args);
        }

        private void OverwriteAndUpdateDb<T>(System.Data.IDbTransaction transaction, string deleteSql, object[] deleteArgs, IEnumerable<T> values, bool updateExisting)
        {
            var properties = GetORMFields<T>();
            var idfield = properties.Where(x => x.Name == "ID").FirstOrDefault();
            properties = properties.Where(x => x.Name != "ID").ToArray();

            string sql;

            if (updateExisting)
            {
                sql = string.Format(
                    @"UPDATE ""{0}"" SET {1} WHERE ""ID""=?",
                    typeof(T).Name,
                    string.Join(@", ", properties.Select(x => string.Format(@"""{0}""=?", x.Name)))
                );

                properties = properties.Union(new System.Reflection.PropertyInfo[] { idfield }).ToArray();
            }
            else
            {
    
                sql = string.Format(
                    @"INSERT INTO ""{0}"" (""{1}"") VALUES ({2})",
                    typeof(T).Name,
                    string.Join(@""", """, properties.Select(x => x.Name)),
                    string.Join(@", ", properties.Select(x => "?"))
                );
            }

            OverwriteAndUpdateDb(transaction, deleteSql, deleteArgs, values, sql, (item) =>
            {
                return properties.Select((x) =>
                {
                    var val = x.GetValue(item, null);

                    if (x.PropertyType.IsEnum)
                        val = val.ToString();
                    else if (x.PropertyType == typeof(DateTime))
                        val = NormalizeDateTimeToEpochSeconds((DateTime)val);

                    return val;                    
                }).ToArray();
            });

            if (!updateExisting && values.Count() == 1 && idfield != null)
                using(var cmd = m_connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"SELECT last_insert_rowid();";
                    if (idfield.PropertyType == typeof(string))
                        idfield.SetValue(values.First(), ExecuteScalarString(cmd), null);
                    else
                        idfield.SetValue(values.First(), ExecuteScalarInt64(cmd), null);
                }
        }
        
        private IEnumerable<T> ReadFromDb<T>(Func<System.Data.IDataReader, T> f, string sql, params object[] args)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.CommandText = sql;
                if (args != null)
                    foreach(var a in args)
                    {
                        var p = cmd.CreateParameter();
                        p.Value = a;
                        cmd.Parameters.Add(p);
                    }
                
                return Read(cmd, f).ToArray();
            }
        }
                
        private void OverwriteAndUpdateDb<T>(System.Data.IDbTransaction transaction, string deleteSql, object[] deleteArgs, IEnumerable<T> values, string insertSql, Func<T, object[]> f)
        {
            using(var cmd = m_connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                
                if (!string.IsNullOrEmpty(deleteSql))
                {
                    cmd.CommandText = deleteSql;
                    if (deleteArgs != null)
                        foreach(var a in deleteArgs)
                        {
                            var p = cmd.CreateParameter();
                            p.Value = a;
                            cmd.Parameters.Add(p);
                        }
                
                    cmd.ExecuteNonQuery();
                    cmd.Parameters.Clear();
                }
                
                cmd.CommandText = insertSql;
                                
                foreach(var n in values)
                {
                    var r = f(n);
                    if (r == null)
                        continue;
                        
                    while (cmd.Parameters.Count < r.Length)
                        cmd.Parameters.Add(cmd.CreateParameter());
                    
                    for(var i = 0; i < r.Length; i++)
                        ((System.Data.IDbDataParameter)cmd.Parameters[i]).Value = r[i];
                            
                    cmd.ExecuteNonQuery();
                }
            }
        }
        
        #region IDisposable implementation
        public void Dispose()
        {
            if (m_errorcmd != null)
                try { if (m_errorcmd != null) m_errorcmd.Dispose(); }
                catch { }
                finally { m_errorcmd = null; }


            try
            {
                if (m_connection != null)
                    m_connection.Dispose();
            }
            catch
            {
            }
        }
        #endregion
    }
    
}

