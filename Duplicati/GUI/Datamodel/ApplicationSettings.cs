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
using System.Data.LightDatamodel;

namespace Duplicati.Datamodel
{
    public class ApplicationSettings
    {
        public enum NotificationLevel
        {
            Off,
            Start,
            StartAndStop,
            Continous,
            Warnings,
            Errors
        }

        private SettingsHelper<ApplicationSetting, string, string> m_appset;

        private const string RECENT_DURATION = "Recent duration";
        private const string TEMP_PATH = "Temp Path";

        private const string USE_COMMON_PASSWORD = "Use common password";
        private const string COMMON_PASSWORD_ENCRYPTION_MODULE = "Encryption module used with common password";
        private const string COMMON_PASSWORD = "Common password";

        private const string SIGNATURE_CACHE_PATH = "Signature Cache Path";
        private const string SIGNATURE_CACHE_ENABLED = "Signature Cache Enabled";

        private const string DISPLAY_LANGUAGE = "Display Language";

        private const string STARTUP_DELAY_DURATION = "Startup delay duration";
        private const string THREAD_PRIORITY_OVERRIDE = "Thread priority override";
        private const string UPLOAD_SPEED_LIMIT = "Upload speed limit";
        private const string DOWNLOAD_SPEED_LIMIT = "Download speed limit";

        private const string HIDE_DONATE_BUTTON = "Hide donate button";

        private const string BALLON_NOTIFICATION_LEVEL = "Balloon notification level";

        public ApplicationSettings(IDataFetcher dataparent)
        {
            m_appset = new SettingsHelper<ApplicationSetting, string, string>(dataparent, new List<ApplicationSetting>(dataparent.GetObjects<ApplicationSetting>()), "Name", "Value");
        }

        /// <summary>
        /// Creates a detached copy of all the application settings
        /// </summary>
        /// <returns>A dictionary with application settings</returns>
        public Dictionary<string, string> CreateDetachedCopy()
        {
            //We need to read/write all values to make sure they have the propper defaults
            foreach (System.Reflection.PropertyInfo pi in this.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
                if (pi.CanRead && pi.CanWrite)
                    pi.SetValue(this, pi.GetValue(this, null), null);

            //After that, simply copy the values over
            Dictionary<string, string> res = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> k in m_appset)
                res[k.Key] = k.Value;
            return res;
        }

        /// <summary>
        /// Gets a reference to the internal options
        /// </summary>
        public IDictionary<string, string> RawOptions { get { return m_appset; } }

        /// <summary>
        /// Gets or sets the amount of time a log entry will be visible in the list of recent backups
        /// </summary>
        public string RecentBackupDuration
        {
            get { return string.IsNullOrEmpty(m_appset[RECENT_DURATION]) ? "2W" : m_appset[RECENT_DURATION]; }
            set { m_appset[RECENT_DURATION] = value; }
        }
        
        /// <summary>
        /// Returns the default Duplicati temp path
        /// </summary>
        public static string DefaultTempPath
        {
            get
            {
#if DEBUG
                return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "TEMP");
#else
                return System.IO.Path.GetTempPath();
#endif
            }
        }

        /// <summary>
        /// Gets or sets the path to store temporary files. May contain environment variables
        /// </summary>
        public string TempPath
        {
            get
            {
                if (string.IsNullOrEmpty(m_appset[TEMP_PATH]))
                {
                    return DefaultTempPath;
                }
                else
                    return m_appset[TEMP_PATH];
            }
            set { m_appset[TEMP_PATH] = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating if Duplicati should apply a common password to backups
        /// </summary>
        public bool UseCommonPassword
        {
            get
            {
                bool res;
                string tmp;
                m_appset.TryGetValue(USE_COMMON_PASSWORD, out tmp);

                if (bool.TryParse(tmp, out res))
                    return res;
                else
                    return false;
            }
            set { m_appset[USE_COMMON_PASSWORD] = value.ToString(); }
        }

        /// <summary>
        /// Gets or sets the encryption module used as default
        /// </summary>
        public string CommonPasswordEncryptionModule
        {
            get
            {
                string res;
                m_appset.TryGetValue(COMMON_PASSWORD_ENCRYPTION_MODULE, out res);
                return string.IsNullOrEmpty(res) ? "aes" : res;
            }
            set { m_appset[COMMON_PASSWORD_ENCRYPTION_MODULE] = value; }
        }

        /// <summary>
        /// Gets or sets the common password applied to encrypt files.
        /// </summary>
        public string CommonPassword
        {
            get
            {
                string res;
                m_appset.TryGetValue(COMMON_PASSWORD, out res);
                return string.IsNullOrEmpty(res) ? "" : res;
            }
            set { m_appset[COMMON_PASSWORD] = value; }
        }
  
        /// <summary>
        /// Returns the default Duplicati signature cache path
        /// </summary>
        public static string DefaultSignatureCachePath
        {
            get
            {
                if (Duplicati.Library.Utility.Utility.IsClientLinux)
                    return System.IO.Path.Combine(Environment.ExpandEnvironmentVariables("%DUPLICATI_HOME%").TrimStart('"').TrimEnd('"'), "Signature Cache");
                else
                    return System.IO.Path.Combine(System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(System.Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), "Duplicati"), "Signature Cache");                
            }
        }
        
        /// <summary>
        /// Gets or sets the path to store signature cache files. May contain environment variables
        /// </summary>
        public string SignatureCachePath
        {
            get
            {
                if (string.IsNullOrEmpty(m_appset[SIGNATURE_CACHE_PATH]))
                {
                    return DefaultSignatureCachePath;
                }
                else
                    return m_appset[SIGNATURE_CACHE_PATH];
            }
            set { m_appset[SIGNATURE_CACHE_PATH] = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating if the signature cache should be used.
        /// </summary>
        public bool SignatureCacheEnabled
        {
            get
            {
                bool res;
                if (bool.TryParse(m_appset[SIGNATURE_CACHE_ENABLED], out res))
                    return res;
                else
                    return true;
            }
            set { m_appset[SIGNATURE_CACHE_ENABLED] = value.ToString(); }
        }

        /// <summary>
        /// Gets or sets the language used in the user interface
        /// </summary>
        public string DisplayLanguage
        {
            get
            {
                if (string.IsNullOrEmpty(m_appset[DISPLAY_LANGUAGE]))
                    return "";
                else
                    return m_appset[DISPLAY_LANGUAGE];
            }
            set { m_appset[DISPLAY_LANGUAGE] = value; }
        }

        /// <summary>
        /// Gets or sets the amount of time Duplicati will be paused after startup
        /// </summary>
        public string StartupDelayDuration
        {
            get { return string.IsNullOrEmpty(m_appset[STARTUP_DELAY_DURATION]) ? "5m" : m_appset[STARTUP_DELAY_DURATION]; }
            set { m_appset[STARTUP_DELAY_DURATION] = value; }
        }

        /// <summary>
        /// Gets or sets the thread priority used for the backup thread
        /// </summary>
        public System.Threading.ThreadPriority? ThreadPriorityOverride
        {
            get
            {
                if (string.IsNullOrEmpty(m_appset[THREAD_PRIORITY_OVERRIDE]))
                    return null;
                else
                    return Library.Utility.Utility.ParsePriority(m_appset[THREAD_PRIORITY_OVERRIDE]);
            }
            set 
            {
                if (value == null)
                    m_appset[THREAD_PRIORITY_OVERRIDE] = "";
                else
                    m_appset[THREAD_PRIORITY_OVERRIDE] = value.Value.ToString();
            }
        }

        /// <summary>
        /// Gets or sets the upper limit for download speeds
        /// </summary>
        public string DownloadSpeedLimit
        {
            get { return m_appset[DOWNLOAD_SPEED_LIMIT]; }
            set { m_appset[DOWNLOAD_SPEED_LIMIT] = value == null ? "" : value; }
        }

        /// <summary>
        /// Gets or sets the upper limit for upload speeds
        /// </summary>
        public string UploadSpeedLimit
        {
            get { return m_appset[UPLOAD_SPEED_LIMIT]; }
            set { m_appset[UPLOAD_SPEED_LIMIT] = value == null ? "" : value; }
        }

        /// <summary>
        /// Gets or sets a value indicating if the donate button in Duplicati should be hidden
        /// </summary>
        public bool HideDonateButton
        {
            get
            {
                string tmp;
                m_appset.TryGetValue(HIDE_DONATE_BUTTON, out tmp);

                return Duplicati.Library.Utility.Utility.ParseBool(tmp, false);
            }
            set { m_appset[HIDE_DONATE_BUTTON] = value.ToString(); }
        }

        /// <summary>
        /// Gets or sets the balloon notification level
        /// </summary>
        public NotificationLevel BallonNotificationLevel
        {
            get
            {
                string v;
                m_appset.TryGetValue(BALLON_NOTIFICATION_LEVEL, out v);
                if (!string.IsNullOrEmpty(v))
                    try { return (NotificationLevel)Enum.Parse(typeof(NotificationLevel), v); }
                    catch { }

                return NotificationLevel.Warnings;
            }
            set { m_appset[BALLON_NOTIFICATION_LEVEL] = value.ToString(); }
        }
    }
}
