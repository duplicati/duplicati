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
using System.Collections.Specialized;
using System.Text;


namespace Duplicati.Datamodel
{
    public partial class Task
    {
        public Task()
        {
            this.AfterDataCommit += new System.Data.LightDatamodel.DataConnectionEventHandler(Task_AfterDataCommit);
        }

        void Task_AfterDataCommit(object sender, System.Data.LightDatamodel.DataActions action)
        {
            if (action != System.Data.LightDatamodel.DataActions.Fetch)
            {
                m_backendSettings = null;
                m_extensionSettings = null;
            }
        }

        private SettingsHelper<BackendSetting, string, string> m_backendSettings;
        private SettingsHelper<TaskExtension, string, string> m_extensionSettings;

        private SettingsHelper<TaskOverride, string, string> m_taskOverrides;

        private SettingsHelper<EncryptionSetting, string, string> m_encryptionSettings; 
        private SettingsHelper<CompressionSetting, string, string> m_compressionSettings;

        public IDictionary<string, string> BackendSettingsLookup
        {
            get
            {
                //Extra check because the datamodel copies the collection between contexts... not nice
                if (m_backendSettings == null || m_backendSettings.DataParent != this.DataParent || m_backendSettings.Collection != this.BackendSettings)
                    m_backendSettings = new SettingsHelper<BackendSetting, string, string>(this.DataParent, this.BackendSettings, "Name", "Value");

                return m_backendSettings;
            }
        }

        public IDictionary<string, string> TaskExtensionsLookup
        {
            get
            {
                //Extra check because the datamodel copies the collection between contexts... not nice
                if (m_extensionSettings == null || m_extensionSettings.DataParent != this.DataParent || m_extensionSettings.Collection != this.TaskExtensions)
                    m_extensionSettings = new SettingsHelper<TaskExtension, string, string>(this.DataParent, this.TaskExtensions, "Name", "Value");

                return m_extensionSettings;
            }
        }

        public IDictionary<string, string> TaskOverridesLookup
        {
            get
            {
                //Extra check because the datamodel copies the collection between contexts... not nice
                if (m_taskOverrides == null || m_taskOverrides.DataParent != this.DataParent || m_taskOverrides.Collection != this.TaskOverrides)
                    m_taskOverrides = new SettingsHelper<TaskOverride, string, string>(this.DataParent, this.TaskOverrides, "Name", "Value");

                return m_taskOverrides;
            }
        }

        public IDictionary<string, string> EncryptionSettingsLookup
        {
            get
            {
                //Extra check because the datamodel copies the collection between contexts... not nice
                if (m_encryptionSettings == null || m_encryptionSettings.DataParent != this.DataParent || m_encryptionSettings.Collection != this.EncryptionSettings)
                    m_encryptionSettings = new SettingsHelper<EncryptionSetting, string, string>(this.DataParent, this.EncryptionSettings, "Name", "Value");

                return m_encryptionSettings;
            }
        }

        public IDictionary<string, string> CompressionSettingsLookup
        {
            get
            {
                //Extra check because the datamodel copies the collection between contexts... not nice
                if (m_compressionSettings == null || m_compressionSettings.DataParent != this.DataParent || m_compressionSettings.Collection != this.CompressionSettings)
                    m_compressionSettings = new SettingsHelper<CompressionSetting, string, string>(this.DataParent, this.CompressionSettings, "Name", "Value");

                return m_compressionSettings;
            }
        }

        public TaskExtensionWrapper Extensions
        {
            get { return new TaskExtensionWrapper(this); }
        }

        public string FilterXml
        {
            get
            {
                if (this.Filters.Count <= 0)
                    return "";

                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                System.Xml.XmlNode root = doc.AppendChild(doc.CreateElement("root"));

                foreach (TaskFilter f in this.SortedFilters)
                {
                    System.Xml.XmlNode e = root.AppendChild(doc.CreateElement("filter"));
                    e.Attributes.Append(doc.CreateAttribute("include")).Value = f.Include.ToString();
                    e.Attributes.Append(doc.CreateAttribute("filter")).Value = f.Filter ?? "";
                    e.Attributes.Append(doc.CreateAttribute("globbing")).Value = f.GlobbingFilter ?? "";
                }
                    
                return doc.OuterXml;
            }
            set
            {
                if (value == this.EncodedFilter)
                    return;

                //Delete previous ones
                this.SortedFilters = new TaskFilter[0];

                if (string.IsNullOrEmpty(value))
                    return;
                
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                doc.LoadXml(value);

                List<TaskFilter> filters = new List<TaskFilter>();
                foreach(System.Xml.XmlNode n in doc.SelectNodes("root/filter")) 
                {
                    TaskFilter tf = this.DataParent.Add<TaskFilter>();
                    tf.Include = bool.Parse(n.Attributes["include"].Value);
                    tf.GlobbingFilter = n.Attributes["globbing"].Value;
                    tf.Filter = n.Attributes["filter"].Value;
                    filters.Add(tf);
                }

                this.SortedFilters = filters.ToArray();
            }
        }

        public string EncodedFilter
        {
            get
            {
                if (this.Filters.Count <= 0)
                    return "";

                List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();
                foreach (TaskFilter f in this.SortedFilters)
                    filters.Add(new KeyValuePair<bool, string>(f.Include, f.Filter));

                return Library.Utility.FilenameFilter.EncodeAsFilter(filters);
            }
        }

        public bool ExistsInDb
        {
            get { return this.ID > 0; }
        }


        public TaskFilter[] SortedFilters
        {
            get
            {
                return System.Data.LightDatamodel.Query.Parse("ORDER BY SortOrder").EvaluateList<TaskFilter>(this.Filters).ToArray();
            }
            set
            {
                foreach (TaskFilter t in this.SortedFilters)
                    if (Array.IndexOf<TaskFilter>(value, t) >= 0)
                        this.Filters.Remove(t);
                    else
                        t.DataParent.DeleteObject(t);

                int index = 0;
                foreach (TaskFilter t in value)
                {
                    t.SortOrder = index++;
                    this.Filters.Add(t);
                }
            }
        }


        public class TaskExtensionWrapper
        {
            private const string MAX_UPLOAD_SIZE = "Max Upload Size";
            private const string UPLOAD_BANDWIDTH = "Upload Bandwidth";
            private const string DOWNLOAD_BANDWIDTH = "Download Bandwidth";
            private const string VOLUME_SIZE = "Volume Size";
            private const string THREAD_PRIORITY = "Thread Priority";
            private const string ASYNC_TRANSFER = "Async Transfer";
            private const string INCLUDE_SETUP = "Include Setup";
            private const string IGNORE_TIMESTAMPS = "Ignore Timestamps";
            private const string FILE_SIZE_LIMIT = "File Size Limit";

            private const string SELECTFILES_VERSION = "Select Files - Version";
            private const string SELECTFILES_USESIMPLEMODE = "Select Files - Use Simple Mode";
            private const string SELECTFILES_INCLUDEDOCUMENTS = "Select Files - Include Documents";
            private const string SELECTFILES_INCLUDEDESKTOP = "Select Files - Include Desktop";
            private const string SELECTFILES_INCLUDEMUSIC = "Select Files - Include Music";
            private const string SELECTFILES_INCLUDEIMAGES = "Select Files - Include Images";
            private const string SELECTFILES_INCLUDEAPPDATA = "Select Files - Include AppData";

            private const string SELECTWHEN_WARNEDNOSCHEDULE = "Select When - Warned No Schedule";
            private const string SELECTWHEN_WARNEDTOOMANYINCREMENTALS = "Select When - Warned Too Many Incrementals";
            private const string SELECTWHEN_WARNEDNOINCREMENTALS = "Select When - Warned No Incrementals";

            private const string PASSWORDSETTINGS_WARNEDNOPASSWORD = "Password Settings - Warned No Password";
            private const string CLEANUPSETTINGS_WARNEDNOCLEANUP = "Cleanup Settings - Warned No Cleanup";

            private const string DISABLE_AES_FALLBACK_DECRYPTION = "Disable AES fallback encryption";

            private Task m_owner;

            public TaskExtensionWrapper(Task owner)
            {
                m_owner = owner;
            }

            public string MaxUploadSize
            {
                get { return m_owner.TaskExtensionsLookup[MAX_UPLOAD_SIZE]; }
                set { m_owner.TaskExtensionsLookup[MAX_UPLOAD_SIZE] = value; }
            }

            public string UploadBandwidth
            {
                get { return m_owner.TaskExtensionsLookup[UPLOAD_BANDWIDTH]; }
                set { m_owner.TaskExtensionsLookup[UPLOAD_BANDWIDTH] = value; }
            }

            public string DownloadBandwidth
            {
                get { return m_owner.TaskExtensionsLookup[DOWNLOAD_BANDWIDTH]; }
                set { m_owner.TaskExtensionsLookup[DOWNLOAD_BANDWIDTH] = value; }
            }

            public string VolumeSize
            {
                get { return m_owner.TaskExtensionsLookup[VOLUME_SIZE]; }
                set { m_owner.TaskExtensionsLookup[VOLUME_SIZE] = value; }
            }

            public string ThreadPriority
            {
                get { return m_owner.TaskExtensionsLookup[THREAD_PRIORITY]; }
                set { m_owner.TaskExtensionsLookup[THREAD_PRIORITY] = value; }
            }

            public bool AsyncTransfer
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[ASYNC_TRANSFER], true); }
                set { m_owner.TaskExtensionsLookup[ASYNC_TRANSFER] = value.ToString(); }
            }

            public bool IncludeSetup
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[INCLUDE_SETUP], true); }
                set { m_owner.TaskExtensionsLookup[INCLUDE_SETUP] = value.ToString(); }
            }

            public bool IgnoreTimestamps
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[IGNORE_TIMESTAMPS], false); }
                set { m_owner.TaskExtensionsLookup[IGNORE_TIMESTAMPS] = value.ToString(); }
            }

            public string FileSizeLimit
            {
                get { return m_owner.TaskExtensionsLookup[FILE_SIZE_LIMIT]; }
                set { m_owner.TaskExtensionsLookup[FILE_SIZE_LIMIT] = value; }
            }

            public int SelectFiles_Version
            {
                get { return string.IsNullOrEmpty(m_owner.TaskExtensionsLookup[SELECTFILES_VERSION]) ? 1 : int.Parse(m_owner.TaskExtensionsLookup[SELECTFILES_VERSION]); }
                set { m_owner.TaskExtensionsLookup[SELECTFILES_VERSION] = value.ToString(); }
            }

            public bool SelectFiles_UseSimpleMode
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTFILES_USESIMPLEMODE], false); }
                set { m_owner.TaskExtensionsLookup[SELECTFILES_USESIMPLEMODE] = value.ToString(); }
            }

            public bool SelectFiles_IncludeDocuments
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEDOCUMENTS], false); }
                set { m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEDOCUMENTS] = value.ToString(); }
            }

            public bool SelectFiles_IncludeDesktop
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEDESKTOP], false); }
                set { m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEDESKTOP] = value.ToString(); }
            }

            public bool SelectFiles_IncludeMusic
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEMUSIC], false); }
                set { m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEMUSIC] = value.ToString(); }
            }

            public bool SelectFiles_IncludeImages
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEIMAGES], false); }
                set { m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEIMAGES] = value.ToString(); }
            }

            public bool SelectFiles_IncludeAppData
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEAPPDATA], false); }
                set { m_owner.TaskExtensionsLookup[SELECTFILES_INCLUDEAPPDATA] = value.ToString(); }
            }

            public bool SelectWhen_WarnedNoSchedule
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTWHEN_WARNEDNOSCHEDULE], false); }
                set { m_owner.TaskExtensionsLookup[SELECTWHEN_WARNEDNOSCHEDULE] = value.ToString(); }
            }

            public bool SelectWhen_WarnedNoIncrementals
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTWHEN_WARNEDNOINCREMENTALS], false); }
                set { m_owner.TaskExtensionsLookup[SELECTWHEN_WARNEDNOINCREMENTALS] = value.ToString(); }
            }

            public bool SelectWhen_WarnedTooManyIncrementals
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[SELECTWHEN_WARNEDTOOMANYINCREMENTALS], false); }
                set { m_owner.TaskExtensionsLookup[SELECTWHEN_WARNEDTOOMANYINCREMENTALS] = value.ToString(); }
            }

            public bool PasswordSettings_WarnedNoPassword
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[PASSWORDSETTINGS_WARNEDNOPASSWORD], false); }
                set { m_owner.TaskExtensionsLookup[PASSWORDSETTINGS_WARNEDNOPASSWORD] = value.ToString(); }
            }

            public bool CleanupSettings_WarnedNoCleanup
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[CLEANUPSETTINGS_WARNEDNOCLEANUP], false); }
                set { m_owner.TaskExtensionsLookup[CLEANUPSETTINGS_WARNEDNOCLEANUP] = value.ToString(); }
            }

            public bool DisableAESFallbackDecryption
            {
                get { return Duplicati.Library.Utility.Utility.ParseBool(m_owner.TaskExtensionsLookup[DISABLE_AES_FALLBACK_DECRYPTION], false); }
                set { m_owner.TaskExtensionsLookup[DISABLE_AES_FALLBACK_DECRYPTION] = value.ToString(); }
            }
        }
    }
}
