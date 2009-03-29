#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

        public TaskExtensionWrapper Extensions
        {
            get { return new TaskExtensionWrapper(this); }
        }

        public Backends.IBackend Backend
        {
            get
            {
                //TODO: This should be more dynamic
                switch (this.Service.Trim().ToLower())
                {
                    case "ssh":
                        return new Backends.SSH(this);
                    case "s3":
                        return new Backends.S3(this);
                    case "file":
                        return new Backends.File(this);
                    case "ftp":
                        return new Backends.FTP(this);
                    case "webdav":
                        return new Backends.WEBDAV(this);
                }

                return null;
            }
        }

        public string GetDestinationPath()
        {
            return this.Backend.GetDestinationPath();
        }

        public void GetOptions(Dictionary<string, string> options)
        {
            this.Backend.GetOptions(options);

            if (this.Filters.Count > 0)
                options["filter"] = this.EncodedFilter; ;
            if (this.GPGEncryption)
            {
                options["gpg-encryption"] = "";
                options["gpg-program-path"] = System.Environment.ExpandEnvironmentVariables(new ApplicationSettings(this.DataParent).GPGPath);
            }

            if (string.IsNullOrEmpty(this.Encryptionkey))
                options.Add("no-encryption", "");
            else
                options.Add("passphrase", this.Encryptionkey);

            ApplicationSettings set = new ApplicationSettings(this.DataParent);
            if (!string.IsNullOrEmpty(set.TempPath))
                options["tempdir"] = System.Environment.ExpandEnvironmentVariables(set.TempPath);

            this.Extensions.GetOptions(options);

            //Override everything set in the overrides
            foreach (TaskOverride ov in this.TaskOverrides)
                options[ov.Name] = ov.Value;
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

                return Library.Core.FilenameFilter.EncodeAsFilter(filters);
            }
            set
            {
                //Delete previous ones
                this.SortedFilters = new TaskFilter[0];

                List<TaskFilter> filters = new List<TaskFilter>();
                foreach (KeyValuePair<bool, string> f in Library.Core.FilenameFilter.DecodeFilter(value))
                {
                    TaskFilter tf = this.DataParent.Add<TaskFilter>();
                    tf.Filter = f.Value;
                    tf.Include = f.Key;
                    filters.Add(tf);
                }

                this.SortedFilters = filters.ToArray();
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
            private const string FILE_TIME_SEPERATOR = "File Time Seperator";
            private const string SHORT_FILENAMES = "Short Filenames";
            private const string FILENAME_PREFIX = "Filename Prefix";

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
                get { return Duplicati.Library.Core.Utility.ParseBool(m_owner.TaskExtensionsLookup[ASYNC_TRANSFER], false); }
                set { m_owner.TaskExtensionsLookup[ASYNC_TRANSFER] = value.ToString(); }
            }

            public bool IncludeSetup
            {
                get { return Duplicati.Library.Core.Utility.ParseBool(m_owner.TaskExtensionsLookup[INCLUDE_SETUP], true); }
                set { m_owner.TaskExtensionsLookup[INCLUDE_SETUP] = value.ToString(); }
            }

            public bool IgnoreTimestamps
            {
                get { return Duplicati.Library.Core.Utility.ParseBool(m_owner.TaskExtensionsLookup[IGNORE_TIMESTAMPS], true); }
                set { m_owner.TaskExtensionsLookup[IGNORE_TIMESTAMPS] = value.ToString(); }
            }

            public string FileSizeLimit
            {
                get { return m_owner.TaskExtensionsLookup[FILE_SIZE_LIMIT]; }
                set { m_owner.TaskExtensionsLookup[FILE_SIZE_LIMIT] = value; }
            }

            public string FileTimeSeperator
            {
                get { return m_owner.TaskExtensionsLookup[FILE_TIME_SEPERATOR]; }
                set { m_owner.TaskExtensionsLookup[FILE_TIME_SEPERATOR] = value; }
            }

            public bool ShortFilenames
            {
                get { return Duplicati.Library.Core.Utility.ParseBool(m_owner.TaskExtensionsLookup[SHORT_FILENAMES], false); }
                set { m_owner.TaskExtensionsLookup[SHORT_FILENAMES] = value.ToString(); }
            }

            public string FilenamePrefix
            {
                get { return m_owner.TaskExtensionsLookup[FILENAME_PREFIX]; }
                set { m_owner.TaskExtensionsLookup[FILENAME_PREFIX] = value; }
            }

            public void GetOptions(Dictionary<string, string> options)
            {
                if (!string.IsNullOrEmpty(this.MaxUploadSize))
                    options["totalsize"] = this.MaxUploadSize;
                if (!string.IsNullOrEmpty(this.VolumeSize))
                    options["volsize"] = this.VolumeSize;
                if (!string.IsNullOrEmpty(this.DownloadBandwidth))
                    options["max-download-pr-second"] = this.DownloadBandwidth;
                if (!string.IsNullOrEmpty(this.UploadBandwidth))
                    options["max-upload-pr-second"] = this.UploadBandwidth;
                if (!string.IsNullOrEmpty(this.ThreadPriority))
                    options["thread-priority"] = this.ThreadPriority;
                if (this.AsyncTransfer)
                    options["asynchronous-upload"] = "";
                if (this.IgnoreTimestamps)
                    options["disable-filetime-check"] = "";
                if (!string.IsNullOrEmpty(this.FileTimeSeperator))
                    options["time-separator"] = this.FileTimeSeperator;
            }
        }
    }
}
