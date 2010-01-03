#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Datamodel;

namespace Duplicati.GUI
{
    public enum DuplicityTaskType
    {
        IncrementalBackup,
        FullBackup,
        RemoveAllButNFull,
        RemoveOlderThan,
        Restore,
        ListBackups,
        ListFiles,
        RestoreSetup,
        ListActualFiles,
        ListBackupEntries
    }


    public delegate void TaskCompletedHandler(IDuplicityTask owner, string output);

    public interface IDuplicityTask
    {
        DuplicityTaskType TaskType { get; }
        event TaskCompletedHandler TaskCompleted;
        void RaiseTaskCompleted(string output);

        
        DateTime BeginTime { get; set; }
        Task Task { get; }
        Schedule Schedule { get; }

        string GetConfiguration(Dictionary<string, string> options); 
        string LocalPath { get; }

        string Result { get; set; }
    }

    public abstract class BackupTask : IDuplicityTask
    {
        protected Schedule m_schedule;
        protected DateTime m_beginTime;

        protected string m_result;

        public abstract DuplicityTaskType TaskType { get; }

        public Schedule Schedule { get { return m_schedule; } }
        public Task Task { get { return m_schedule.Task; } }
        public event TaskCompletedHandler TaskCompleted;
        public virtual DateTime BeginTime 
        {
            get { return m_beginTime; }
            set { m_beginTime = value; }
        }

        public BackupTask(Schedule schedule)
        {
            m_schedule = schedule;
        }

        public void RaiseTaskCompleted(string output)
        {
            if (TaskCompleted != null)
                TaskCompleted(this, output);
        }

        public abstract string LocalPath { get; }
        public virtual string GetConfiguration(Dictionary<string, string> options)
        {
            this.Schedule.GetOptions(options);
            return this.Task.GetConfiguration(options);
        }

        public string Result
        {
            get { return m_result; }
            set { m_result = value; }
        }
    }

    public abstract class FullOrIncrementalTask : BackupTask
    {
        protected FullOrIncrementalTask(Schedule schedule)
            : base(schedule)
        {
            base.TaskCompleted += new TaskCompletedHandler(DoneEvent);
        }

        protected void DoneEvent(IDuplicityTask task, string output)
        {
            lock (Program.MainLock)
            {
                Log l = this.Task.DataParent.Add<Log>();
                LogBlob lb = this.Task.DataParent.Add<LogBlob>();
                lb.StringData = output;

                l.Blob = lb;
                this.Task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicatiOutputParser.ParseData(l);
                l.SubAction = "Primary";
                l.Action = "Backup";
                l.BeginTime = m_beginTime;
                l.EndTime = DateTime.Now;

                (this.Task.DataParent as System.Data.LightDatamodel.IDataFetcherCached).CommitAll();
                if (this.Task.DataParent != Program.DataConnection)
                    Program.DataConnection.CommitAll();
            }
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);
            if (!string.IsNullOrEmpty(this.Task.Signaturekey))
                options.Add("sign-key", this.Task.Signaturekey);
            
            return destination;
        }

        public override string LocalPath
        {
            get { return System.Environment.ExpandEnvironmentVariables(this.Task.SourcePath); }
        }
    }

    public class FullBackupTask : FullOrIncrementalTask
    {
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.FullBackup; } }

        public FullBackupTask(Schedule schedule)
            : base(schedule)
        {
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);
            options.Add("full", "");
            return destination;
        }

    }

    public class IncrementalBackupTask : FullOrIncrementalTask
    {
        private string m_fullAfter = null;

        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.IncrementalBackup; } }

        public string FullAfter
        {
            get { return m_fullAfter == null ? m_schedule.Task.FullAfter : m_fullAfter; }
        }

        public IncrementalBackupTask(Schedule schedule, string fullAfter)
            : this(schedule)
        {
            m_fullAfter = fullAfter;
        }

        public IncrementalBackupTask(Schedule schedule)
            : base(schedule)
        {
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);
            options.Add("incremental", "");

            if (!string.IsNullOrEmpty(this.FullAfter))
                options.Add("full-if-older-than", this.FullAfter);

            return destination;
        }
    }

    public class ListBackupEntriesTask : BackupTask
    {
        private List<Library.Main.BackupEntry> m_backups = null;
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.ListBackupEntries; } }
        public List<Library.Main.BackupEntry> Backups { get { return m_backups; } set { m_backups = value; } }

        public ListBackupEntriesTask(Schedule schedule)
            : base(schedule)
        {
        }

        public override string LocalPath
        {
            get { throw new NotImplementedException(); }
        }
    }

    public class ListBackupsTask : BackupTask
    {
        private string[] m_backups = null;
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.ListBackups; } }
        public string[] Backups { get { return m_backups; } set { m_backups = value; } }

        public ListBackupsTask(Schedule schedule)
            : base(schedule)
        {
            this.TaskCompleted += new TaskCompletedHandler(ListBackupsTask_TaskCompleted);
        }

        void ListBackupsTask_TaskCompleted(IDuplicityTask owner, string output)
        {
            if (string.IsNullOrEmpty(output))
                return;

            const string DIVIDER = "-------------------------\r\n";
            const string HEADERS = " Type of backup set:                            Time:      Num volumes:";

            List<string> res = new List<string>();

            foreach (string part in output.Split(new string[] { DIVIDER }, StringSplitOptions.RemoveEmptyEntries))
                if (part.IndexOf(HEADERS) >= 0)
                {
                    bool passedHeader = false;
                    foreach (string line in part.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith(HEADERS))
                            passedHeader = true;
                        else if (passedHeader)
                        {
                            string tmp = line.Trim();
                            if (tmp.StartsWith("Full"))
                                tmp = tmp.Substring("Full".Length).Trim();
                            else if (tmp.StartsWith("Incremental"))
                                tmp = tmp.Substring("Incremental".Length).Trim();

                            string datetime = tmp.Substring(0, tmp.LastIndexOf(' ')).Trim();
                            /*DateTime dt;
                            if (DateTime.TryParse(datetime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))*/
                            res.Add(datetime);
                        }
                    }
                }


            //System.Text.RegularExpressions.Regex regexp = new System.Text.RegularExpressions.Regex("----------------

            m_backups = res.ToArray();
        }

        public override string LocalPath
        {
            get { throw new NotImplementedException(); }
        }
    }

    public class ListFilesTask : BackupTask
    {
        protected IList<string> m_files = null;
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.ListFiles; } }
        public IList<string> Files { get { return m_files; } set { m_files = value; } }
        protected DateTime m_when;

        public ListFilesTask(Schedule schedule, DateTime when)
            : base(schedule)
        {
            m_when = when;
        }

        public string When
        {
            get
            {
                if (m_when.Year < 10)
                    return null;
                else
                    return m_when.ToString("s");
            }
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);

            if (!string.IsNullOrEmpty(When))
                options["restore-time"] = this.When;

            return destination;
        }

        public override string LocalPath
        {
            get { throw new NotImplementedException(); }
        }
    }

    public class ListActualFilesTask : BackupTask
    {
        protected List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>> m_files = null;
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.ListActualFiles; } }
        public List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>> Files { get { return m_files; } set { m_files = value; } }
        protected DateTime m_when;

        public ListActualFilesTask(Schedule schedule, DateTime when)
            : base(schedule)
        {
            m_when = when;
        }

        public string When
        {
            get
            {
                if (m_when.Year < 10)
                    return null;
                else
                    return m_when.ToString("s");
            }
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);

            if (!string.IsNullOrEmpty(When))
                options["restore-time"] = this.When;

            return destination;
        }

        public override string LocalPath
        {
            get { throw new NotImplementedException(); }
        }
    }


    public class RestoreSetupTask : BackupTask
    {
        protected string m_targetdir = null;

        public RestoreSetupTask(Schedule schedule, string targetdir)
            : base(schedule)
        {
            m_targetdir = targetdir;
        }

        public override string LocalPath
        {
            get { return System.Environment.ExpandEnvironmentVariables(this.TargetDir); }
        }

        public string TargetDir { get { return m_targetdir; } }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);
            if (options.ContainsKey("filter"))
                options.Remove("filter");

            return destination;
        }

        public override DuplicityTaskType TaskType
        {
            get { return DuplicityTaskType.RestoreSetup; }
        }
    }

    public class RestoreTask : BackupTask
    {
        protected DateTime m_when;
        protected string m_targetdir = null;
        protected string m_sourceFiles = null;

        public string When
        {
            get
            {
                if (m_when.Year < 10)
                    return null;
                else
                    return m_when.ToString("s");
            }
        }

        public string TargetDir { get { return m_targetdir; } }
        public string SourceFiles { get { return m_sourceFiles; } }

        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.Restore; } }

        public RestoreTask(Schedule schedule, string targetdir, string sourcefiles)
            : this(schedule, targetdir, sourcefiles, new DateTime())
        {
        }

        public RestoreTask(Schedule schedule, string targetdir, DateTime when)
            : this(schedule, targetdir, null, when)
        {
        }

        public RestoreTask(Schedule schedule, string targetdir)
            : this(schedule, targetdir, null, new DateTime())
        {
        }

        public RestoreTask(Schedule schedule, string targetdir, string sourcefiles, DateTime when)
            : base(schedule)
        {
            m_targetdir = targetdir;
            m_sourceFiles = sourcefiles;
            m_when = when;
            base.TaskCompleted += new TaskCompletedHandler(DoneEvent);
        }

        void DoneEvent(IDuplicityTask owner, string output)
        {
            lock (Program.MainLock)
            {
                Log l = this.Task.DataParent.Add<Log>();
                LogBlob lb = this.Task.DataParent.Add<LogBlob>();
                lb.StringData = output;

                l.Blob = lb;
                this.Task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicatiOutputParser.ParseData(l);
                l.SubAction = "Primary";
                l.Action = "Restore";
                l.BeginTime = m_beginTime;
                l.EndTime = DateTime.Now;

                (this.Task.DataParent as System.Data.LightDatamodel.IDataFetcherCached).CommitAll();
                Program.DataConnection.CommitAll();
            }
        }

        public override string LocalPath
        {
            get { return System.Environment.ExpandEnvironmentVariables(this.TargetDir); }
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);

            options.Add("force", "");
            if (!string.IsNullOrEmpty(When))
                options.Add("restore-time", this.When);

            if (!string.IsNullOrEmpty(this.SourceFiles))
                options.Add("file-to-restore", this.SourceFiles);

            return destination;
        }
    }

    public class RemoveAllButNFullTask : BackupTask
    {
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.RemoveAllButNFull; } }
        private int m_fullCount;
        public int FullCount { get { return m_fullCount; } }

        public RemoveAllButNFullTask(Schedule schedule, int fullcount)
            : base(schedule)
        {
            m_fullCount = fullcount;
            base.TaskCompleted += new TaskCompletedHandler(DoneEvent);
        }

        void DoneEvent(IDuplicityTask owner, string output)
        {
            lock (Program.MainLock)
            {
                Log l = this.Task.DataParent.Add<Log>();
                LogBlob lb = this.Task.DataParent.Add<LogBlob>();
                lb.StringData = output;

                l.Blob = lb;
                this.Task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicatiOutputParser.ParseData(l);
                l.SubAction = "Cleanup";
                l.Action = "Backup";
                l.BeginTime = m_beginTime;
                l.EndTime = DateTime.Now;

                (this.Task.DataParent as System.Data.LightDatamodel.IDataFetcherCached).CommitAll();
                if (this.Task.DataParent != Program.DataConnection)
                    Program.DataConnection.CommitAll();
            }            
        }

        public override string LocalPath
        {
            get 
            {
                throw new MissingMethodException();
            }
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);

            options.Add("remove-all-but-n-full", this.FullCount.ToString());
            options.Add("force", "");
            if (!options.ContainsKey("number-of-retries"))
                options["number-of-retries"] = "2";

            return destination;
        }
    }

    public class RemoveOlderThanTask : BackupTask
    {
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.RemoveOlderThan; } }

        private string m_older;
        public string Older { get { return m_older; } }

        public RemoveOlderThanTask(Schedule schedule, string older)
            : base(schedule)
        {
            m_older = older;
            base.TaskCompleted += new TaskCompletedHandler(DoneEvent);
        }

        void DoneEvent(IDuplicityTask owner, string output)
        {
            lock (Program.MainLock)
            {
                Log l = this.Task.DataParent.Add<Log>();
                LogBlob lb = this.Task.DataParent.Add<LogBlob>();
                lb.StringData = output;

                l.Blob = lb;
                this.Task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicatiOutputParser.ParseData(l);
                l.SubAction = "Cleanup";
                l.Action = "Backup";
                l.BeginTime = m_beginTime;
                l.EndTime = DateTime.Now;
            }
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);

            options.Add("remove-older-than", this.Older);
            options.Add("force", "");
            if (!options.ContainsKey("number-of-retries"))
                options["number-of-retries"] = "2";

            return destination;
        }

        public override string LocalPath
        {
            get { throw new NotImplementedException(); }
        }
    }
}
