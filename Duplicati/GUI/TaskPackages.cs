#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
        ListSourceFolders,
        RestoreSetup,
        ListActualFiles,
        ListBackupEntries
    }


    public delegate void TaskCompletedHandler(IDuplicityTask owner, string output, string shortMessage);

    public interface IDuplicityTask
    {
        DuplicityTaskType TaskType { get; }
        event TaskCompletedHandler TaskCompleted;
        void RaiseTaskCompleted(string output, string parsedMessage);

        DateTime BeginTime { get; set; }
        Task Task { get; }
        Schedule Schedule { get; }

        string GetConfiguration(Dictionary<string, string> options); 
        string LocalPath { get; }

        string Result { get; set; }

        bool IsAborted { get; set; }
    }

    public abstract class BackupTask : IDuplicityTask
    {
        protected Schedule m_schedule;
        protected DateTime m_beginTime;

        protected string m_result;
        protected bool m_isAborted = false;

        public bool IsAborted { get { return m_isAborted; } set { m_isAborted = value; } }
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

        public void RaiseTaskCompleted(string output, string parsedMessage)
        {
            if (TaskCompleted != null)
                TaskCompleted(this, output, parsedMessage);
        }

        public abstract string LocalPath { get; }

        public virtual string GetConfiguration(Dictionary<string, string> options)
        {
            //Schedule settings have lowest priority, because there is currently no setup
            SetupSchedule(options);

            //Now setup the environment
            ApplicationSettings appSet = new ApplicationSettings(this.Task.DataParent);

            if (this.Task.ExistsInDb && appSet.SignatureCacheEnabled && !string.IsNullOrEmpty(appSet.SignatureCachePath))
                options["signature-cache-path"] = System.IO.Path.Combine(System.Environment.ExpandEnvironmentVariables(appSet.SignatureCachePath), this.Task.Schedule.ID.ToString());

            if (!string.IsNullOrEmpty(appSet.TempPath))
            {
                string tempdir = System.Environment.ExpandEnvironmentVariables(appSet.TempPath);
                if (!System.IO.Directory.Exists(tempdir))
                    System.IO.Directory.CreateDirectory(tempdir);

                options["tempdir"] = tempdir;
            }

            Dictionary<string, string> env = appSet.CreateDetachedCopy();

            //Inject the encryption, backend and compression module names into the environment
            env["encryption-module"] = this.Task.EncryptionModule;
            env["compression-module"] = this.Task.CompressionModule;
            env["backend-module"] = this.Task.Service;

            //If there are any control extensions, let them modify the environment
            foreach (Library.Interface.ISettingsControl ic in Library.DynamicLoader.SettingsControlLoader.Modules)
                ic.GetConfiguration(env, SettingExtension.GetExtensions(this.Task.Schedule.DataParent, ic.Key), options);

            //Setup encryption module
            SetupEncryptionModule(env, this.Task.EncryptionSettingsLookup, options);

            //Setup compression module
            SetupCompressionModule(env, this.Task.CompressionSettingsLookup, options);

            //Next is the actual backend setup
            string destination = SetupBackend(env, options);

            //Setup any task options
            SetupTask(options);

            //Setup any task extension options
            SetupTaskExtensions(options);

            //Override everything set in the overrides, this is placed last so it cannot be overriden elsewhere
            foreach (TaskOverride ov in this.Task.TaskOverrides)
                options[ov.Name] = ov.Value;

            return destination;
        }

        protected virtual void SetupEncryptionModule(Dictionary<string, string> env, IDictionary<string, string> guiOptions, Dictionary<string, string> options)
        {
            foreach (Library.Interface.IEncryption e in Library.DynamicLoader.EncryptionLoader.Modules)
                if (e.FilenameExtension == this.Task.EncryptionModule)
                {
                    if (e is Library.Interface.IEncryptionGUI)
                    {
                        (e as Library.Interface.IEncryptionGUI).GetConfiguration(env, guiOptions, options);
                    }
                    else
                    {
                        foreach (string k in guiOptions.Keys)
                            if (k.StartsWith("--"))
                                options[k.Substring(2)] = guiOptions[k];
                    }

                    return;
                }
        }

        protected virtual void SetupCompressionModule(Dictionary<string, string> env, IDictionary<string, string> guiOptions, Dictionary<string, string> options)
        {
            foreach (Library.Interface.ICompression e in Library.DynamicLoader.CompressionLoader.Modules)
                if (e.FilenameExtension == this.Task.CompressionModule)
                {
                    if (e is Library.Interface.ICompressionGUI)
                    {
                        (e as Library.Interface.ICompressionGUI).GetConfiguration(env, guiOptions, options);
                    }
                    else
                    {
                        foreach (string k in guiOptions.Keys)
                            if (k.StartsWith("--"))
                                options[k.Substring(2)] = guiOptions[k];
                    }

                    return;
                }
        }

        protected virtual void SetupSchedule(Dictionary<string, string> options)
        {
        }

        protected virtual void SetupTask(Dictionary<string, string> options)
        {
            if (this.Task.Filters.Count > 0)
                options["filter"] = this.Task.EncodedFilter;

            if (!string.IsNullOrEmpty(this.Task.EncryptionModule))
                options["encryption-module"] = this.Task.EncryptionModule;

            if (string.IsNullOrEmpty(this.Task.Encryptionkey))
                options["no-encryption"] = "";
            else
                options["passphrase"] = this.Task.Encryptionkey;
        }

        protected virtual void SetupTaskExtensions(Dictionary<string, string> options)
        {
            Datamodel.Task.TaskExtensionWrapper ext = this.Task.Extensions;

            if (!string.IsNullOrEmpty(ext.MaxUploadSize))
                options["totalsize"] = ext.MaxUploadSize;
            if (!string.IsNullOrEmpty(ext.VolumeSize))
                options["volsize"] = ext.VolumeSize;
            if (!string.IsNullOrEmpty(ext.DownloadBandwidth))
                options["max-download-pr-second"] = ext.DownloadBandwidth;
            if (!string.IsNullOrEmpty(ext.UploadBandwidth))
                options["max-upload-pr-second"] = ext.UploadBandwidth;
            if (!string.IsNullOrEmpty(ext.ThreadPriority))
                options["thread-priority"] = ext.ThreadPriority;
            if (!ext.AsyncTransfer)
                options["synchronous-upload"] = "true";
            if (ext.IgnoreTimestamps)
                options["disable-filetime-check"] = "";

            if (!string.IsNullOrEmpty(ext.FileSizeLimit))
                options["skip-files-larger-than"] = ext.FileSizeLimit;

            //Bit tricky, but we REALLY want to disallow fallback decryption for new backups
            //We need some extra protection, otherwise the user will be warned about an unused option,
            // if they have non-AES encryption or no encryption at all.
            //This means that we have special support for the "aes" module in here, which is bad encapsulation
            //Once we completely remove fallback encryption this check can be removed
            if (!string.IsNullOrEmpty(this.Task.Encryptionkey) && "aes".Equals(this.Task.EncryptionModule, StringComparison.InvariantCultureIgnoreCase) && ext.DisableAESFallbackDecryption)
                options["aes-encryption-dont-allow-fallback"] = "true";
        }

        protected virtual string SetupBackend(Dictionary<string, string> environment, Dictionary<string, string> options)
        {
            Library.Interface.IBackend selectedBackend = null;
            foreach (Library.Interface.IBackend b in Library.DynamicLoader.BackendLoader.Backends)
                if (b.ProtocolKey == this.Task.Service)
                {
                    selectedBackend = b;
                    break;
                }

            if (selectedBackend == null)
                throw new Exception("Missing backend");

            string destination;
            if (selectedBackend is Library.Interface.IBackendGUI)
            {
                //Simply invoke the backends setup function
                destination = ((Library.Interface.IBackendGUI)selectedBackend).GetConfiguration(environment, this.Task.BackendSettingsLookup, options);
            }
            else
            {
                //We store destination with the key "Destination" and other options with the -- prefix
                if (!options.ContainsKey(Duplicati.GUI.Wizard_pages.GridContainer.DESTINATION_EXTENSION_KEY))
                    throw new Exception("Invalid configuration");

                destination = options[Duplicati.GUI.Wizard_pages.GridContainer.DESTINATION_EXTENSION_KEY];
                foreach (KeyValuePair<string, string> k in this.Task.BackendSettingsLookup)
                    if (k.Key.StartsWith("--")) //All options are prefixed with this
                        options[k.Key.Substring(2)] = k.Value;
            }

            return destination;
        }

        public string Result
        {
            get { return m_result; }
            set { m_result = value; }
        }

        protected virtual Log WriteLogMessage(string action, string subaction, string data, string parsedMessage, bool commit, Log log)
        {
            lock (Program.MainLock)
            {
                System.Data.LightDatamodel.IDataFetcher con;
                LogBlob lb;

                if (log == null)
                {
                    con = this.Task.DataParent;
                    log = con.Add<Log>();
                    lb = con.Add<LogBlob>();

                    log.Blob = lb;
                    this.Task.Logs.Add(log);
                }
                else
                {
                    con = log.DataParent;
                    if (log.Blob == null)
                        log.Blob = con.Add<LogBlob>();
                    
                    lb = log.Blob;
                }

                lb.StringData = data;

                //Keep some of the data in an easy to read manner
                DuplicatiOutputParser.ParseData(log);


                log.Action = action;
                log.SubAction = subaction;
                log.BeginTime = m_beginTime;
                log.EndTime = DateTime.Now;
                log.ParsedMessage = parsedMessage;

                if (parsedMessage == "InProgress")
                {
                    log.ParsedStatus = DuplicatiOutputParser.InterruptedStatus;
                    log.ParsedMessage = "";
                }


                if (commit)
                    (con as System.Data.LightDatamodel.IDataFetcherWithRelations).CommitAllRecursive();
            }

            return log;
        }
    }

    public abstract class FullOrIncrementalTask : BackupTask
    {
        protected Log m_log = null;
        public Log LogEntry { get { return m_log; } set { m_log = value; } }

        protected FullOrIncrementalTask(Schedule schedule)
            : base(schedule)
        {
            base.TaskCompleted += new TaskCompletedHandler(DoneEvent);
        }

        protected void DoneEvent(IDuplicityTask task, string output, string parsedMessage)
        {
            WriteLogMessage("Backup", "Primary", output, parsedMessage, true, m_log); 
            m_log = null;
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            return base.GetConfiguration(options);
        }

        public override string LocalPath
        {
            get { return System.Environment.ExpandEnvironmentVariables(this.Task.SourcePath); }
        }

        internal void WriteBackupInProgress(string message)
        {
            m_log = WriteLogMessage("Backup", "InProgress", message, "InProgress", true, null);
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
            options["full"] = "";
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

            if (!(string.IsNullOrEmpty(this.FullAfter) || options.ContainsKey("full-if-older-than")))
                options.Add("full-if-older-than", this.FullAfter);

            return destination;
        }
    }

    public class ListBackupEntriesTask : BackupTask
    {
        private List<Library.Main.ManifestEntry> m_backups = null;
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.ListBackupEntries; } }
        public List<Library.Main.ManifestEntry> Backups { get { return m_backups; } set { m_backups = value; } }

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

        void ListBackupsTask_TaskCompleted(IDuplicityTask owner, string output, string parsedMessage)
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

    public class ListSourceFoldersTask : ListFilesTask
    {
        public override DuplicityTaskType TaskType { get { return DuplicityTaskType.ListSourceFolders; } }
        public ListSourceFoldersTask(Schedule schedule, DateTime when)
            : base(schedule, when)
        {
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

        void DoneEvent(IDuplicityTask owner, string output, string parsedMessage)
        {
            WriteLogMessage("Restore", "Primary", output, parsedMessage, true, null);
        }

        public override string LocalPath
        {
            get { return System.Environment.ExpandEnvironmentVariables(this.TargetDir); }
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);

            if (!options.ContainsKey("force"))
                options.Add("force", "");

            if (!string.IsNullOrEmpty(When))
                options["restore-time"] = this.When;

            if (!string.IsNullOrEmpty(this.SourceFiles))
                options["file-to-restore"] = this.SourceFiles;

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

        void DoneEvent(IDuplicityTask owner, string output, string parsedMessage)
        {
            WriteLogMessage("Backup", "Cleanup", output, parsedMessage, false, null);
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

            options["delete-all-but-n-full"] = this.FullCount.ToString();
            if (!options.ContainsKey("force"))
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

        void DoneEvent(IDuplicityTask owner, string output, string parsedMessage)
        {
            WriteLogMessage("Backup", "Cleanup", output, parsedMessage, false, null);
        }

        public override string GetConfiguration(Dictionary<string, string> options)
        {
            string destination = base.GetConfiguration(options);

            options["delete-older-than"] = this.Older;
            if (!options.ContainsKey("force"))
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
