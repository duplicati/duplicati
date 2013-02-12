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

namespace Duplicati.GUI.Wizard_pages
{
    /// <summary>
    /// This class wraps all settings available in the wizard pages
    /// </summary>
    public class WizardSettingsWrapper
    {
        /*
         * Most of the properties in this class are just copies
         * of the actual data object properties and most of this
         * class could be removed if they mapped directly to
         * the data objects instead.
         */

        private Dictionary<string, object> m_settings;
        private const string PREFIX = "WSW_";

        public enum MainAction
        {
            Unknown,
            Add,
            Edit,
            Restore,
            Remove,
            RunNow,
            RestoreSetup
        };

        public WizardSettingsWrapper(Dictionary<string, object> settings)
        {
            m_settings = settings;
        }

        /// <summary>
        /// The purpose of this function is to set the default
        /// settings on the new backup.
        /// </summary>
        public void SetupDefaults()
        {
            m_settings.Clear();

            ApplicationSettings appset = new ApplicationSettings(Program.DataConnection);
            if (appset.UseCommonPassword)
            {
                this.BackupPassword = appset.CommonPassword;
                this.EncryptionModule = appset.CommonPasswordEncryptionModule;
            }

            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.Load(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Program), "Backup defaults.xml"));

            System.Xml.XmlNode root = doc.SelectSingleNode("settings");

            List<System.Xml.XmlNode> nodes = new List<System.Xml.XmlNode>();

            if (root != null)
                foreach (System.Xml.XmlNode n in root.ChildNodes)
                    nodes.Add(n);

            //Load user supplied settings, if any
            string filename = System.IO.Path.Combine(System.Windows.Forms.Application.StartupPath, "Backup defaults.xml");
            if (System.IO.File.Exists(filename))
            {
                doc.Load(filename);
                root = doc.SelectSingleNode("settings");
                if (root != null)
                    foreach (System.Xml.XmlNode n in root.ChildNodes)
                        nodes.Add(n);
            }

            foreach (System.Xml.XmlNode n in nodes)
                if (n.NodeType == System.Xml.XmlNodeType.Element)
                {
                    System.Reflection.PropertyInfo pi = this.GetType().GetProperty(n.Name);
                    if (pi != null && pi.CanWrite)
                        if (pi.PropertyType == typeof(DateTime))
                            pi.SetValue(this, Library.Utility.Timeparser.ParseTimeInterval(n.InnerText, DateTime.Now.Date), null);
                        else
                            pi.SetValue(this, Convert.ChangeType(n.InnerText, pi.PropertyType), null);
                }

            this.ApplicationSettings = ApplicationSetup.GetApplicationSettings(Program.DataConnection);
        }

        /// <summary>
        /// Clears all settings, and makes the setting object reflect the schedule
        /// </summary>
        /// <param name="schedule">The schedule to reflect</param>
        public void ReflectSchedule(Datamodel.Schedule schedule)
        {
            MainAction action = this.PrimayAction;
            System.Data.LightDatamodel.IDataFetcherWithRelations connection = this.DataConnection;
            m_settings.Clear();

            this.ScheduleID = schedule.ID;
            this.ScheduleName = schedule.Name;
            this.SchedulePath = schedule.Path;
            this.SourcePath = schedule.Task.SourcePath;
            this.EncodedFilterXml = schedule.Task.FilterXml;
            this.BackupPassword = schedule.Task.Encryptionkey;

            this.Backend = schedule.Task.Service;
            this.BackendSettings = new Dictionary<string, string>(schedule.Task.BackendSettingsLookup); 

            this.BackupTimeOffset = schedule.When;
            this.RepeatInterval = schedule.Repeat;
            this.AllowedWeekdays = schedule.AllowedWeekdays;
            this.FullBackupInterval = schedule.Task.FullAfter;
            this.MaxFullBackups = (int)schedule.Task.KeepFull;
            this.BackupExpireInterval = schedule.Task.KeepTime;
            this.UploadSpeedLimit = schedule.Task.Extensions.UploadBandwidth;
            this.DownloadSpeedLimit = schedule.Task.Extensions.DownloadBandwidth;
            this.BackupSizeLimit = schedule.Task.Extensions.MaxUploadSize;
            this.VolumeSize = schedule.Task.Extensions.VolumeSize;
            this.ThreadPriority = schedule.Task.Extensions.ThreadPriority;
            this.AsyncTransfer = schedule.Task.Extensions.AsyncTransfer;
            this.EncryptionModule = schedule.Task.EncryptionModule;
            this.IncludeSetup = schedule.Task.IncludeSetup;
            this.IgnoreFileTimestamps = schedule.Task.Extensions.IgnoreTimestamps;
            this.FileSizeLimit = schedule.Task.Extensions.FileSizeLimit;
            this.DisableAESFallbackEncryption = schedule.Task.Extensions.DisableAESFallbackDecryption;

            //Handle the "Select Files" portion
            this.SelectFilesUI.Version = schedule.Task.Extensions.SelectFiles_Version;
            this.SelectFilesUI.UseSimpleMode = schedule.Task.Extensions.SelectFiles_UseSimpleMode;
            this.SelectFilesUI.IncludeDocuments = schedule.Task.Extensions.SelectFiles_IncludeDocuments;
            this.SelectFilesUI.IncludeDesktop = schedule.Task.Extensions.SelectFiles_IncludeDesktop;
            this.SelectFilesUI.IncludeMusic = schedule.Task.Extensions.SelectFiles_IncludeMusic;
            this.SelectFilesUI.IncludeImages = schedule.Task.Extensions.SelectFiles_IncludeImages;
            this.SelectFilesUI.IncludeSettings = schedule.Task.Extensions.SelectFiles_IncludeAppData;

            this.SelectWhenUI.HasWarnedNoSchedule = schedule.Task.Extensions.SelectWhen_WarnedNoSchedule;
            this.SelectWhenUI.HasWarnedTooManyIncremental = schedule.Task.Extensions.SelectWhen_WarnedTooManyIncrementals;
            this.SelectWhenUI.HasWarnedNoIncrementals = schedule.Task.Extensions.SelectWhen_WarnedNoIncrementals;

            this.PasswordSettingsUI.WarnedNoPassword = schedule.Task.Extensions.PasswordSettings_WarnedNoPassword;

            this.CleanupSettingsUI.HasWarnedClean = schedule.Task.Extensions.CleanupSettings_WarnedNoCleanup;

            this.Overrides = new Dictionary<string, string>(schedule.Task.TaskOverridesLookup);
            this.EncryptionSettings = new Dictionary<string, string>(schedule.Task.EncryptionSettingsLookup);
            this.CompressionSettings = new Dictionary<string, string>(schedule.Task.CompressionSettingsLookup);
            this.ApplicationSettings = ApplicationSetup.GetApplicationSettings(schedule.DataParent);

            this.PrimayAction = action;
            this.DataConnection = connection;
        }


        /// <summary>
        /// Writes all values from the session object back into a schedule object
        /// </summary>
        /// <param name="schedule"></param>
        public void UpdateSchedule(Datamodel.Schedule schedule)
        {
            schedule.Name = this.ScheduleName;
            schedule.Path = this.SchedulePath;
            if (schedule.Task == null)
                schedule.Task = schedule.DataParent.Add<Datamodel.Task>();
            schedule.Task.SourcePath = this.SourcePath;
            schedule.Task.FilterXml = this.EncodedFilterXml;
            schedule.Task.Encryptionkey = this.BackupPassword;

            schedule.Task.Service = this.Backend;
            SyncLookupTables(this.BackendSettings, schedule.Task.BackendSettingsLookup);

            schedule.When = this.BackupTimeOffset;
            schedule.Repeat = this.RepeatInterval;
            schedule.AllowedWeekdays = this.AllowedWeekdays;
            schedule.Task.FullAfter = this.FullBackupInterval;
            schedule.Task.KeepFull = this.MaxFullBackups;
            
            schedule.Task.KeepTime = this.BackupExpireInterval;
            schedule.Task.Extensions.UploadBandwidth = this.UploadSpeedLimit;
            schedule.Task.Extensions.DownloadBandwidth = this.DownloadSpeedLimit;
            schedule.Task.Extensions.MaxUploadSize = this.BackupSizeLimit;

            schedule.Task.Extensions.VolumeSize = this.VolumeSize;
            schedule.Task.Extensions.ThreadPriority = this.ThreadPriority;
            schedule.Task.Extensions.AsyncTransfer = this.AsyncTransfer;

            schedule.Task.EncryptionModule = this.EncryptionModule;
            schedule.Task.IncludeSetup = this.IncludeSetup;
            schedule.Task.Extensions.IgnoreTimestamps = this.IgnoreFileTimestamps;
            schedule.Task.Extensions.FileSizeLimit = this.FileSizeLimit;
            schedule.Task.Extensions.DisableAESFallbackDecryption = this.DisableAESFallbackEncryption;

            schedule.Task.Extensions.SelectFiles_Version = this.SelectFilesUI.Version;
            schedule.Task.Extensions.SelectFiles_UseSimpleMode = this.SelectFilesUI.UseSimpleMode;
            schedule.Task.Extensions.SelectFiles_IncludeDocuments = this.SelectFilesUI.IncludeDocuments;
            schedule.Task.Extensions.SelectFiles_IncludeDesktop = this.SelectFilesUI.IncludeDesktop;
            schedule.Task.Extensions.SelectFiles_IncludeMusic = this.SelectFilesUI.IncludeMusic;
            schedule.Task.Extensions.SelectFiles_IncludeImages = this.SelectFilesUI.IncludeImages;
            schedule.Task.Extensions.SelectFiles_IncludeAppData = this.SelectFilesUI.IncludeSettings;

            schedule.Task.Extensions.SelectWhen_WarnedNoSchedule = this.SelectWhenUI.HasWarnedNoSchedule;
            schedule.Task.Extensions.SelectWhen_WarnedTooManyIncrementals = this.SelectWhenUI.HasWarnedTooManyIncremental;
            schedule.Task.Extensions.SelectWhen_WarnedNoIncrementals = this.SelectWhenUI.HasWarnedNoIncrementals;

            schedule.Task.Extensions.PasswordSettings_WarnedNoPassword = this.PasswordSettingsUI.WarnedNoPassword;

            schedule.Task.Extensions.CleanupSettings_WarnedNoCleanup = this.CleanupSettingsUI.HasWarnedClean;

            SyncLookupTables(this.Overrides, schedule.Task.TaskOverridesLookup);

            SyncLookupTables(this.EncryptionSettings, schedule.Task.EncryptionSettingsLookup);
            SyncLookupTables(this.CompressionSettings, schedule.Task.CompressionSettingsLookup);

            ApplicationSetup.SaveExtensionSettings(schedule.DataParent, this.ApplicationSettings);
        }

        /// <summary>
        /// Synchronizes two lookup tables
        /// </summary>
        /// <param name="source">The desired table contents</param>
        /// <param name="target">The current table contents</param>
        private void SyncLookupTables(IDictionary<string, string> source, IDictionary<string, string> target)
        {
            //TODO: Should rewrite this to use the IDictionary instances directly, 
            // this would require a nested dataconnection for new entries
            foreach (KeyValuePair<string, string> p in source)
                target[p.Key] = p.Value;

            foreach (string k in new List<string>(target.Keys))
                if (!source.ContainsKey(k))
                    target.Remove(k);
        }

        /// <summary>
        /// Internal helper to typecast the values, and protect agains missing values
        /// </summary>
        /// <typeparam name="T">The type of the value stored</typeparam>
        /// <param name="key">The key used to identify the setting</param>
        /// <param name="default">The value to use if there is no value stored</param>
        /// <returns>The value or the default value</returns>
        public T GetItem<T>(string key, T @default)
        {
            return m_settings.ContainsKey(PREFIX + key) ? (T)m_settings[PREFIX + key] : @default;
        }

        public void SetItem(string key, object value)
        {
            m_settings[PREFIX + key] = value;
        }

        /// <summary>
        /// The action taken on the primary page
        /// </summary>
        public MainAction PrimayAction
        {
            get { return GetItem<MainAction>("PrimaryAction", MainAction.Unknown); }
            set { SetItem("PrimaryAction", value); }
        }

        /// <summary>
        /// The ID of the schedule being edited, if any
        /// </summary>
        public long ScheduleID
        {
            get { return GetItem<long>("ScheduleID", 0); }
            set { SetItem("ScheduleID", value); }
        }

        /// <summary>
        /// Gets or sets the DataConnection used for temp operations
        /// </summary>
        public System.Data.LightDatamodel.IDataFetcherWithRelations DataConnection
        {
            get { return GetItem<System.Data.LightDatamodel.IDataFetcherWithRelations>("DataConnection", null); }
            set { SetItem("DataConnection", value); }
        }

        /// <summary>
        /// The name assigned to the backup
        /// </summary>
        public string ScheduleName
        {
            get { return GetItem<string>("ScheduleName", ""); }
            set { SetItem("ScheduleName", value); }
        }

        /// <summary>
        /// The group path of the backup
        /// </summary>
        public string SchedulePath
        {
            get { return GetItem<string>("SchedulePath", ""); }
            set { SetItem("SchedulePath", value); }
        }

        /// <summary>
        /// The path of the files to be backed up
        /// </summary>
        public string SourcePath
        {
            get { return GetItem<string>("SourcePath", ""); }
            set { SetItem("SourcePath", value); }
        }

        /// <summary>
        /// The password that protects the backup
        /// </summary>
        public string BackupPassword
        {
            get { return GetItem<string>("BackupPassword", ""); }
            set { SetItem("BackupPassword", value); }
        }

        /// <summary>
        /// The currently active backend type
        /// </summary>
        public string Backend
        {
            get { return GetItem<string>("Backend", ""); }
            set { SetItem("Backend", value); }
        }

        /// <summary>
        /// Gets the current settings for the backend
        /// </summary>
        public IDictionary<string, string> BackendSettings
        {
            get 
            {
                if (GetItem<IDictionary<string, string>>("BackendSettings", null) == null)
                    this.BackendSettings = new Dictionary<string, string>();
                return GetItem<IDictionary<string, string>>("BackendSettings", null); 
            }
            set { SetItem("BackendSettings", value); }
        }

        /// <summary>
        /// The offset for running backups
        /// </summary>
        public DateTime BackupTimeOffset
        {
            get { return GetItem<DateTime>("BackupTimeOffset", DateTime.Now); }
            set { SetItem("BackupTimeOffset", value); }
        }

        /// <summary>
        /// The interval at which to repeat the backup
        /// </summary>
        public string RepeatInterval
        {
            get { return GetItem<string>("RepeatInterval", ""); }
            set { SetItem("RepeatInterval", value); }
        }

        /// <summary>
        /// The weekdays at which a backup is allowed to run
        /// </summary>
        public DayOfWeek[] AllowedWeekdays
        {
            get { return GetItem<DayOfWeek[]>("AllowedWeekdays", null); }
            set { SetItem("AllowedWeekdays", value); }
        }

        /// <summary>
        /// The interval at which to perform full backups
        /// </summary>
        public string FullBackupInterval
        {
            get { return GetItem<string>("FullBackupInterval", ""); }
            set { SetItem("FullBackupInterval", value); }
        }

        /// <summary>
        /// The number om full backups to keep
        /// </summary>
        public int MaxFullBackups
        {
            get { return GetItem<int>("MaxFullBackups", 0); }
            set { SetItem("MaxFullBackups", value); }
        }

        /// <summary>
        /// The interval after which backups are deleted
        /// </summary>
        public string BackupExpireInterval
        {
            get { return GetItem<string>("BackupExpireInterval", ""); }
            set { SetItem("BackupExpireInterval", value); }
        }

        /// <summary>
        /// The interval at which to perform full backups
        /// </summary>
        public string UploadSpeedLimit
        {
            get { return GetItem<string>("UploadSpeedLimit", ""); }
            set { SetItem("UploadSpeedLimit", value); }
        }

        /// <summary>
        /// The interval at which to perform full backups
        /// </summary>
        public string DownloadSpeedLimit
        {
            get { return GetItem<string>("DownloadSpeedLimit", ""); }
            set { SetItem("DownloadSpeedLimit", value); }
        }

        /// <summary>
        /// The max size the set of backup files may occupy
        /// </summary>
        public string BackupSizeLimit
        {
            get { return GetItem<string>("BackupSizeLimit", ""); }
            set { SetItem("BackupSizeLimit", value); }
        }

        /// <summary>
        /// The size of each volume in the backup set
        /// </summary>
        public string VolumeSize
        {
            get { return GetItem<string>("VolumeSize", ""); }
            set { SetItem("VolumeSize", value); }
        }

        /// <summary>
        /// The maximum size of files included in the backup
        /// </summary>
        public string FileSizeLimit
        {
            get { return GetItem<string>("FileSizeLimit", ""); }
            set { SetItem("FileSizeLimit", value); }
        }

        /// <summary>
        /// The size of each volume in the backup set
        /// </summary>
        public string ThreadPriority
        {
            get { return GetItem<string>("ThreadPriority", ""); }
            set { SetItem("ThreadPriority", value); }
        }

        /// <summary>
        /// Allow async transfer of files
        /// </summary>
        public bool AsyncTransfer
        {
            get { return GetItem<bool>("AsyncTransfer", false); }
            set { SetItem("AsyncTransfer", value); }
        }

        /// <summary>
        /// Disables AES fallback encryption
        /// </summary>
        public bool DisableAESFallbackEncryption
        {
            get { return GetItem<bool>("DisableAESFallbackEncryption", false); }
            set { SetItem("DisableAESFallbackEncryption", value); }
        }

        /// <summary>
        /// The filter applied to files being backed up
        /// </summary>
        public string EncodedFilterXml
        {
            get { return GetItem<string>("EncodedFilterXml", ""); }
            set { SetItem("EncodedFilterXml", value); }
        }

        /// <summary>
        /// A value indicating if the created/edited backup should run immediately
        /// </summary>
        public bool RunImmediately
        {
            get { return GetItem<bool>("RunImmediately", false); }
            set { SetItem("RunImmediately", value); }
        }

        /// <summary>
        /// A value indicating if the backup should be forced full
        /// </summary>
        public bool ForceFull
        {
            get { return GetItem<bool>("ForceFull", false); }
            set { SetItem("ForceFull", value); }
        }

        /// <summary>
        /// A value indicating the backup to restore
        /// </summary>
        public DateTime RestoreTime
        {
            get { return GetItem<DateTime>("RestoreTime", new DateTime()); }
            set { SetItem("RestoreTime", value); }
        }

        /// <summary>
        /// A value indicating where to place the restored files
        /// </summary>
        public string DisplayRestorePath
        {
            get { return GetItem<string>("RestorePath", ""); }
            set { SetItem("RestorePath", value); }
        }

        /// <summary>
        /// Gets a set of folders to restore to
        /// </summary>
        public string FullRestorePath
        {
            get
            {
                //If some paths are unused, we map them to the temporary folder
                string[] targets = DisplayRestorePath.Split(System.IO.Path.PathSeparator);
                for (int i = 0; i < targets.Length; i++)
                    if (string.IsNullOrEmpty(targets[i]))
                        targets[i] = Environment.ExpandEnvironmentVariables(new Datamodel.ApplicationSettings(Program.DataConnection).TempPath);
                return String.Join(System.IO.Path.PathSeparator.ToString(), targets);
            }
        }

        /// <summary>
        /// A value indicating the filter applied to the restored files
        /// </summary>
        public string RestoreFilter
        {
            get { return GetItem<string>("RestoreFilter", ""); }
            set { SetItem("RestoreFilter", value); }
        }

        /// <summary>
        /// A cached list of filenames in backup
        /// </summary>
        public List<string> RestoreFileList
        {
            get { return GetItem<List<string>>("RestoreFileList:" + this.RestoreTime.ToString(), null); }
            set { SetItem("RestoreFileList:" + this.RestoreTime.ToString(), value); }
        }

        /// <summary>
        /// A cached list of filenames selected by the user
        /// </summary>
        public List<string> RestoreFileSelection
        {
            get { return GetItem<List<string>>("RestoreFileSelection:" + this.RestoreTime.ToString(), null); }
            set { SetItem("RestoreFileSelection:" + this.RestoreTime.ToString(), value); }
        }

        /// <summary>
        /// A list of restore targets
        /// </summary>
        public List<string> RestoreTargetFolders
        {
            get { return GetItem<List<string>>("RestoreTargetFolders:" + this.RestoreTime.ToString(), null); }
            set { SetItem("RestoreTargetFolders:" + this.RestoreTime.ToString(), value); }
        }

        /// <summary>
        /// The encryption module used
        /// </summary>
        public string EncryptionModule
        {
            get { return GetItem<string>("EncryptionModule", "aes"); }
            set { SetItem("EncryptionModule", value); }
        }

        /// <summary>
        /// The compression module used
        /// </summary>
        public string CompressionModule
        {
            get { return GetItem<string>("CompressionModule", "zip"); }
            set { SetItem("CompressionModule", value); }
        }

        /// <summary>
        /// True if the Duplicati setup database should be included
        /// </summary>
        public bool IncludeSetup
        {
            get { return GetItem<bool>("IncludeSetup", false); }
            set { SetItem("IncludeSetup", value); }
        }

        /// <summary>
        /// True if the Duplicati setup database should be included
        /// </summary>
        public bool UseEncryptionAsDefault
        {
            get { return GetItem<bool>("UseEncryptionAsDefault", false); }
            set { SetItem("UseEncryptionAsDefault", value); }
        }

        /// <summary>
        /// True if the File Timestamps should be ignored
        /// </summary>
        public bool IgnoreFileTimestamps
        {
            get { return GetItem<bool>("IgnoreFileTimestamps", false); }
            set { SetItem("IgnoreFileTimestamps", value); }
        }

        /// <summary>
        /// True if advanced restore options should be displayed
        /// </summary>
        public bool ShowAdvancedRestoreOptions
        {
            get { return GetItem<bool>("ShowAdvancedRestoreOptions", false); }
            set { SetItem("ShowAdvancedRestoreOptions", value); }
        }

        /// <summary>
        /// Gets all the overrides present on the task
        /// </summary>
        public IDictionary<string, string> Overrides
        {
            get 
            {
                if (GetItem<IDictionary<string, string>>("Overrides", null) == null)
                    this.Overrides = new Dictionary<string, string>();
                return GetItem<IDictionary<string, string>>("Overrides", null); 
            }
            set { SetItem("Overrides", value); }
        }

        /// <summary>
        /// Gets a wrapper for the settings that are available on the SelectFiles UI
        /// </summary>
        public SelectFilesUI SelectFilesUI { get { return new SelectFilesUI(this); } }

        /// <summary>
        /// Gets a wrapper for the settings that are available on the SelectWhen UI
        /// </summary>
        public SelectWhenUI SelectWhenUI { get { return new SelectWhenUI(this); } }

        /// <summary>
        /// Gets a wrapper for the settings that are available on the CleanupSettings UI
        /// </summary>
        public CleanupSettingsUI CleanupSettingsUI { get { return new CleanupSettingsUI(this); } }

        /// <summary>
        /// Gets a wrapper for the settings that are available on the PasswordSettings UI
        /// </summary>
        public PasswordSettingsUI PasswordSettingsUI { get { return new PasswordSettingsUI(this); } }

        /// <summary>
        /// Gets all the encryption settings present on the task
        /// </summary>
        public IDictionary<string, string> EncryptionSettings
        {
            get
            {
                if (GetItem<IDictionary<string, string>>("EncryptionSettings", null) == null)
                    this.EncryptionSettings = new Dictionary<string, string>();
                return GetItem<IDictionary<string, string>>("EncryptionSettings", null);
            }
            set { SetItem("EncryptionSettings", value); }
        }

        /// <summary>
        /// Gets all the compression settings present on the task
        /// </summary>
        public IDictionary<string, string> CompressionSettings
        {
            get
            {
                if (GetItem<IDictionary<string, string>>("CompressionSettings", null) == null)
                    this.CompressionSettings = new Dictionary<string, string>();
                return GetItem<IDictionary<string, string>>("CompressionSettings", null);
            }
            set { SetItem("CompressionSettings", value); }
        }

        /// <summary>
        /// Gets the application settings
        /// </summary>
        public IDictionary<string, string> ApplicationSettings
        {
            get
            {
                if (GetItem<IDictionary<string, string>>("ApplicationSettings", null) == null)
                    this.ApplicationSettings = new ApplicationSettings(this.DataConnection ?? Program.DataConnection).CreateDetachedCopy();
                return GetItem<IDictionary<string, string>>("ApplicationSettings", null);
            }
            set { SetItem("ApplicationSettings", value); }
        }
    
    }


    /// <summary>
    /// Represents settings that are required to give a consistent view of the file selection UI
    /// </summary>
    public class SelectFilesUI
    {
        private WizardSettingsWrapper m_parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectFilesUI"/> class.
        /// </summary>
        /// <param name="parent">The parent.</param>
        public SelectFilesUI(WizardSettingsWrapper parent)
        {
            m_parent = parent;
        }

        /// <summary>
        /// Gets or sets the version. Used to detect if the user has upgraded.
        /// </summary>
        /// <value>The version.</value>
        public int Version
        {
            get { return m_parent.GetItem<int>("UI:SelectFiles:Version", 1); }
            set { m_parent.SetItem("UI:SelectFiles:Version", value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the user has selected the &quot;Documents&quot; option.
        /// </summary>
        public bool UseSimpleMode
        {
            get { return m_parent.GetItem<bool>("UI:SelectFiles:UseSimpleMode", true); }
            set { m_parent.SetItem("UI:SelectFiles:UseSimpleMode", value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the documents folder is included.
        /// </summary>
        public bool IncludeDocuments
        {
            get { return m_parent.GetItem<bool>("UI:SelectFiles:IncludeDocuments", true); }
            set { m_parent.SetItem("UI:SelectFiles:IncludeDocuments", value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the music folder is included.
        /// </summary>
        public bool IncludeMusic
        {
            get { return m_parent.GetItem<bool>("UI:SelectFiles:IncludeMusic", true); }
            set { m_parent.SetItem("UI:SelectFiles:IncludeMusic", value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the images folder is included.
        /// </summary>
        public bool IncludeImages
        {
            get { return m_parent.GetItem<bool>("UI:SelectFiles:IncludeImages", true); }
            set { m_parent.SetItem("UI:SelectFiles:IncludeImages", value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the desktop folder is included.
        /// </summary>
        public bool IncludeDesktop
        {
            get { return m_parent.GetItem<bool>("UI:SelectFiles:IncludeDesktop", true); }
            set { m_parent.SetItem("UI:SelectFiles:IncludeDesktop", value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the settings folder is included.
        /// </summary>
        public bool IncludeSettings
        {
            get { return m_parent.GetItem<bool>("UI:SelectFiles:IncludeSettings", true); }
            set { m_parent.SetItem("UI:SelectFiles:IncludeSettings", value); }
        }
    }

    /// <summary>
    /// Represents a set of settings related to the SelectWhen wizard page
    /// </summary>
    public class SelectWhenUI
    {
        private WizardSettingsWrapper m_parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectWhenUI"/> class.
        /// </summary>
        /// <param name="parent">The parent.</param>
        public SelectWhenUI(WizardSettingsWrapper parent)
        {
            m_parent = parent;
        }

        /// <summary>
        /// Gets or sets a value indicating if the user has been warned that there is no schedule
        /// </summary>
        public bool HasWarnedNoSchedule
        {
            get { return m_parent.GetItem<bool>("UI:SelectWhen:HasWarnedNoSchedule", false); }
            set { m_parent.SetItem("UI:SelectWhen:HasWarnedNoSchedule", value); }
        }

        /// <summary>
        /// Gets or sets a value indicating if the user has been warned about not selecting incremental backups
        /// </summary>
        public bool HasWarnedNoIncrementals
        {
            get { return m_parent.GetItem<bool>("UI:SelectWhen:HasWarnedNoIncrementals", false); }
            set { m_parent.SetItem("UI:SelectWhen:HasWarnedNoIncrementals", value); }
        }

        /// <summary>
        /// Gets or sets a value indicating if the user has been warned that the settings generate too many incrementals
        /// </summary>
        public bool HasWarnedTooManyIncremental
        {
            get { return m_parent.GetItem<bool>("UI:SelectWhen:HasWarnedTooManyIncremental", false); }
            set { m_parent.SetItem("UI:SelectWhen:HasWarnedTooManyIncremental", value); }
        }

    }

    /// <summary>
    /// Represents the persisted settings for the CleanupSettings UI
    /// </summary>
    public class CleanupSettingsUI
    {
        private WizardSettingsWrapper m_parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="CleanupSettingsUI"/> class.
        /// </summary>
        /// <param name="parent">The parent.</param>
        public CleanupSettingsUI(WizardSettingsWrapper parent)
        {
            m_parent = parent;
        }

        /// <summary>
        /// Gets or sets a value indicating if the user has been warned that cleanup is disabled
        /// </summary>
        public bool HasWarnedClean
        {
            get { return m_parent.GetItem<bool>("UI:CleanupSettings:WarnedClean", false); }
            set { m_parent.SetItem("UI:CleanupSettings:WarnedClean", value); }
        }
    }

    /// <summary>
    /// Represents a set of settings related to the SelectWhen wizard page
    /// </summary>
    public class PasswordSettingsUI
    {
        private WizardSettingsWrapper m_parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordSettingsUI"/> class.
        /// </summary>
        /// <param name="parent">The parent.</param>
        public PasswordSettingsUI(WizardSettingsWrapper parent)
        {
            m_parent = parent;
        }

        /// <summary>
        /// Gets or sets a value indicating if the user has been warned that the backup is not encrypted
        /// </summary>
        public bool WarnedNoPassword
        {
            get { return m_parent.GetItem<bool>("UI:PasswordSettings:WarnedNoPassword", false); }
            set { m_parent.SetItem("UI:PasswordSettings:WarnedNoPassword", value); }
        }
    }

    
}
