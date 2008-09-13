#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati
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
    }

    public delegate void TaskCompletedHandler(IDuplicityTask owner, string output);

    public interface IDuplicityTask
    {
        DuplicityTaskType TaskType { get; }
        event TaskCompletedHandler TaskCompleted;
        void RaiseTaskCompleted(string output);
        void GetArguments(List<string> args);
        DateTime BeginTime { get; set; }
        Task Task { get; }
        Schedule Schedule { get; }
    }

    public abstract class BackupTask : IDuplicityTask
    {
        protected Schedule m_schedule;
        protected DateTime m_beginTime;
        public abstract DuplicityTaskType TaskType { get; }
        public abstract void GetArguments(List<string> args);
        public Schedule Schedule { get { return m_schedule; } }
        public Task Task { get { return m_schedule.Tasks[0]; } }
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

                l.LogBlob = lb;
                this.Task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicityOutputParser.ParseData(l);
                l.SubAction = "Primary";
                l.Action = "Backup";
                l.BeginTime = m_beginTime;
                l.EndTime = DateTime.Now;

                this.Task.DataParent.CommitAll();
                Program.DataConnection.CommitAll();
            }
        }

        public override void GetArguments(List<string> args)
        {
            args.Add("\"" + System.Environment.ExpandEnvironmentVariables(this.Task.SourcePath) + "\"");
            args.Add("\"" + System.Environment.ExpandEnvironmentVariables(this.Task.GetDestinationPath()) + "\"");

            if (!string.IsNullOrEmpty(this.Task.Signaturekey))
            {
                args.Add("--sign-key");
                args.Add(this.Task.Signaturekey);
            }

        }
    }

    public class FullBackupTask : FullOrIncrementalTask
    {
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.FullBackup; } }

        public FullBackupTask(Schedule schedule)
            : base(schedule)
        {
        }

        public override void GetArguments(List<string> args)
        {
            args.Add("full");
            base.GetArguments(args);
        }

    }

    public class IncrementalBackupTask : FullOrIncrementalTask
    {
        private string m_fullAfter = null;

        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.IncrementalBackup; } }

        public string FullAfter
        {
            get { return m_fullAfter == null ? m_schedule.FullAfter : m_fullAfter; }
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

        public override void GetArguments(List<string> args)
        {
            args.Add("incremental");
            base.GetArguments(args);
            if (!string.IsNullOrEmpty(this.FullAfter))
            {
                args.Add("--full-if-older-than");
                args.Add(this.FullAfter);
            }
        }
    }

    public class ListBackupsTask : BackupTask
    {
        private string[] m_backups = null;
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.ListBackups; } }
        public string[] Backups { get { return m_backups; } }

        public ListBackupsTask(Schedule schedule)
            : base(schedule)
        {
            this.TaskCompleted += new TaskCompletedHandler(ListBackupsTask_TaskCompleted);
        }

        void ListBackupsTask_TaskCompleted(IDuplicityTask owner, string output)
        {
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

        public override void GetArguments(List<string> args)
        {
            args.Add("collection-status");
            args.Add("\"" + System.Environment.ExpandEnvironmentVariables(this.Task.GetDestinationPath()) + "\"");
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

                l.LogBlob = lb;
                this.Task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicityOutputParser.ParseData(l);
                l.SubAction = "Primary";
                l.Action = "Restore";
                l.BeginTime = m_beginTime;
                l.EndTime = DateTime.Now;

                this.Task.DataParent.CommitAll();
                Program.DataConnection.CommitAll();
            }
        }

        public override void GetArguments(List<string> args)
        {
            if (!string.IsNullOrEmpty(When))
            {
                args.Add("-t");
                args.Add(this.When);
            }

            if (!string.IsNullOrEmpty(this.SourceFiles))
            {
                args.Add("--file");
                args.Add(this.SourceFiles);
            }

            args.Add("--force");
            args.Add("\"" + System.Environment.ExpandEnvironmentVariables(this.Task.GetDestinationPath()) + "\"");
            args.Add("\"" + System.Environment.ExpandEnvironmentVariables(this.TargetDir) + "\"");
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

                l.LogBlob = lb;
                this.Task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicityOutputParser.ParseData(l);
                l.SubAction = "Cleanup";
                l.Action = "Backup";
                l.BeginTime = m_beginTime;
                l.EndTime = DateTime.Now;
            }            
        }

        public override void GetArguments(List<string> args)
        {
            args.Add("remove-all-but-n-full");
            args.Add(this.FullCount.ToString());
            args.Add("\"" + System.Environment.ExpandEnvironmentVariables(this.Task.GetDestinationPath()) + "\"");
            args.Add("--force");
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

                l.LogBlob = lb;
                this.Task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicityOutputParser.ParseData(l);
                l.SubAction = "Cleanup";
                l.Action = "Backup";
                l.BeginTime = m_beginTime;
                l.EndTime = DateTime.Now;
            }
        }

        public override void GetArguments(List<string> args)
        {
            args.Add("remove-older-than");
            args.Add(this.Older);
            args.Add("\"" + System.Environment.ExpandEnvironmentVariables(this.Task.GetDestinationPath()) + "\"");
            args.Add("--force");
        }

    }
}
