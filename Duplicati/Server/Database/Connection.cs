//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
        
        public event EventHandler DataChanged;
        
        public Connection(System.Data.IDbConnection connection)
        {
            m_connection = connection;
            m_errorcmd = m_connection.CreateCommand();
            m_errorcmd.CommandText = @"INSERT INTO ""ErrorLog"" (""BackupID"", ""Message"", ""Exception"", ""Timestamp"") VALUES (?,?,?,?)";
            for(var i = 0; i < 4; i++)
                m_errorcmd.Parameters.Add(m_errorcmd.CreateParameter());
            
            this.ApplicationSettings = new ApplicationSettings(this);
        }
        
        internal void LogError(long backupid, string message, Exception ex)
        {
            lock(m_lock)
            {
                ((System.Data.IDbDataParameter)m_errorcmd.Parameters[0]).Value = backupid;
                ((System.Data.IDbDataParameter)m_errorcmd.Parameters[1]).Value = message;
                ((System.Data.IDbDataParameter)m_errorcmd.Parameters[2]).Value = ex == null ? null : ex.ToString();
                ((System.Data.IDbDataParameter)m_errorcmd.Parameters[3]).Value = NormalizeDateTimeToEpochSeconds(DateTime.UtcNow);
                m_errorcmd.ExecuteNonQuery();
            }
        }
        
        public ApplicationSettings ApplicationSettings { get; private set; }
        
        internal IDictionary<string, string> GetMetadata(long id)
        {
            lock(m_lock)
                return ReadFromDb(
                    (rd) => new KeyValuePair<string, string>(
                        ConvertToString(rd.GetValue(0)),
                        ConvertToString(rd.GetValue(1))
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
                        Order = ConvertToInt64(rd.GetValue(0)),
                        Include = ConvertToBoolean(rd.GetValue(1)),
                        Expression = ConvertToString(rd.GetValue(2)) ?? ""
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
                        Filter = ConvertToString(rd.GetValue(0)) ?? "",
                        Name = ConvertToString(rd.GetValue(1)) ?? "",
                        Value = ConvertToString(rd.GetValue(2)) ?? ""
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
                        (f) => new object[] { id, f.Filter ?? "", f.Name, f.Value }
                    );            
                    
                    if (tr != null)
                        tr.Commit();
                }
        }
        
        internal string[] GetSources(long id)
        {
            lock(m_lock)
                return ReadFromDb(
                    (rd) => ConvertToString(rd.GetValue(0)),
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
                    
                    return Read(cmd, (rd) => ConvertToInt64(rd.GetValue(0))).ToArray();
                }
        }
        
        internal IBackup GetBackup(long id)
        {
            lock(m_lock)
            {
                var bk = ReadFromDb(
                    (rd) => new Backup() {
                        ID = ConvertToInt64(rd.GetValue(0)),
                        Name = ConvertToString(rd.GetValue(1)),
                        Tags = (ConvertToString(rd.GetValue(2)) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                        TargetURL = ConvertToString(rd.GetValue(3)),
                        DBPath = ConvertToString(rd.GetValue(4)),
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
                        ID = ConvertToInt64(rd.GetValue(0)),
                        Tags = (ConvertToString(rd.GetValue(1)) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                        Time = ConvertToDateTime(rd.GetValue(2)),
                        Repeat = ConvertToString(rd.GetValue(3)),
                        LastRun = ConvertToDateTime(rd.GetValue(4)),
                        Rule = ConvertToString(rd.GetValue(5)),
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
                    
                    return Read(cmd, (rd) => ConvertToInt64(rd.GetValue(0))).ToArray();
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

        private void AddOrUpdateBackup(IBackup item, bool updateSchedule, ISchedule schedule)
        {
            lock(m_lock)
            {
                bool update = item.ID >= 0;
                if (!update && item.DBPath == null)
                {
                    var folder = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Duplicati");
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
                        update ? @"DELETE FROM ""Backup"" WHERE ""ID"" = ?" : null,
                        new object[] { item.ID },
                        new IBackup[] { item },
                        update ?
                            @"UPDATE ""Backup"" SET ""Name""=?, ""Tags""=?, ""TargetURL""=? WHERE ""ID""=?" :
                            @"INSERT INTO ""Backup"" (""Name"", ""Tags"", ""TargetURL"", ""DBPath"") VALUES (?,?,?,?)",
                        (n) => new object[] {
                            n.Name,
                            string.Join(",", n.Tags),
                            n.TargetURL,
                            update ? (object)item.ID : (object)n.DBPath
                        });
                        
                    if (!update)
                        using(var cmd = m_connection.CreateCommand())
                        {
                            cmd.Transaction = tr;
                            cmd.CommandText = @"SELECT last_insert_rowid();";
                            item.ID = ConvertToInt64(cmd.ExecuteScalar());
                        }
                        
                    SetSources(item.Sources, item.ID, tr);
                    SetSettings(item.Settings, item.ID, tr);
                    SetFilters(item.Filters, item.ID, tr);
                    SetMetadata(item.Metadata, item.ID, tr);
                    
                    if (updateSchedule)
                    {
                        var tags = new string[] { "ID=" + item.ID.ToString() }; 
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
                }
        }
        
        private void AddOrUpdateSchedule(ISchedule item, System.Data.IDbTransaction tr)
        {
            lock(m_lock)
            {
                bool update = item.ID >= 0;
                OverwriteAndUpdateDb(
                    tr,
                    update ? @"DELETE FROM ""Schedule"" WHERE ""ID"" = ?" : null,
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
                        n.Rule,
                        update ? (object)item.ID : null
                    });
                    
                if (!update)
                    using(var cmd = m_connection.CreateCommand())
                    {
                        cmd.Transaction = tr;
                        cmd.CommandText = @"SELECT last_insert_rowid();";
                        item.ID = ConvertToInt64(cmd.ExecuteScalar());
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
        }
        
        public void DeleteBackup(IBackup backup)
        {
            DeleteBackup(backup.ID);
        }
        
        public void DeleteSchedule(long ID)
        {
            if (ID < 0)
                return;
            
            lock(m_lock)
                DeleteFromDb("Schedule", ID);
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
                            ID = ConvertToInt64(rd.GetValue(0)),
                            Name = ConvertToString(rd.GetValue(1)),
                            Tags = (ConvertToString(rd.GetValue(2)) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                            TargetURL = ConvertToString(rd.GetValue(3)),
                            DBPath = ConvertToString(rd.GetValue(4)),
                        },
                        @"SELECT ""ID"", ""Name"", ""Tags"", ""TargetURL"", ""DBPath"" FROM ""Backup"" ")
                        .ToArray();
                        
                    foreach(var n in lst)
                        n.Metadata = GetMetadata(n.ID);
                        
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
                            ID = ConvertToInt64(rd.GetValue(0)),
                            Tags = (ConvertToString(rd.GetValue(1)) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                            Time = ConvertToDateTime(rd.GetValue(2)),
                            Repeat = ConvertToString(rd.GetValue(3)),
                            LastRun = ConvertToDateTime(rd.GetValue(4)),
                            Rule = ConvertToString(rd.GetValue(5)),
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
        
        private static long NormalizeDateTimeToEpochSeconds(DateTime input)
        {
            return (long)Math.Floor((NormalizeDateTime(input) - Library.Utility.Utility.EPOCH).TotalSeconds);
        }
        
        private static DateTime ConvertToDateTime(object r)
        {
            var unixTime = ConvertToInt64(r);
            if (unixTime == 0)
                return new DateTime(0);
            
            return Library.Utility.Utility.EPOCH.AddSeconds(unixTime);
        }
        
        private static bool ConvertToBoolean(object r)
        {
            return ConvertToInt64(r) == 1;
        }
        
        private static string ConvertToString(object r)
        {
            if (r == null || r == DBNull.Value)
                return null;
            else
                return r.ToString();
        }
        
        private static long ConvertToInt64(object r, long @default = 0)
        {
            if (r == null || r == DBNull.Value)
                return @default;
            else
                return Convert.ToInt64(r);
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

