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
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Duplicati.Library.Common;

namespace Duplicati.Server.Database
{
    public class ServerSettings
    {
        private static class CONST
        {
            public const string STARTUP_DELAY = "startup-delay";
            public const string DOWNLOAD_SPEED_LIMIT = "max-download-speed";
            public const string UPLOAD_SPEED_LIMIT = "max-upload-speed";
            public const string THREAD_PRIORITY = "thread-priority";
            public const string LAST_WEBSERVER_PORT = "last-webserver-port";
            public const string IS_FIRST_RUN = "is-first-run";
            public const string SERVER_PORT_CHANGED = "server-port-changed";
            public const string SERVER_PASSPHRASE = "server-passphrase";
            public const string SERVER_PASSPHRASE_SALT = "server-passphrase-salt";
            public const string SERVER_PASSPHRASETRAYICON = "server-passphrase-trayicon";
            public const string SERVER_PASSPHRASETRAYICONHASH = "server-passphrase-trayicon-hash";
            public const string UPDATE_CHECK_LAST = "last-update-check";
            public const string UPDATE_CHECK_INTERVAL = "update-check-interval";
            public const string UPDATE_CHECK_NEW_VERSION = "update-check-latest";
            public const string UNACKED_ERROR = "unacked-error";
            public const string UNACKED_WARNING = "unacked-warning";
            public const string SERVER_LISTEN_INTERFACE = "server-listen-interface";
            public const string SERVER_SSL_CERTIFICATE = "server-ssl-certificate";
            public const string HAS_FIXED_INVALID_BACKUPID = "has-fixed-invalid-backup-id";
            public const string UPDATE_CHANNEL = "update-channel";
            public const string USAGE_REPORTER_LEVEL = "usage-reporter-level";
			public const string HAS_ASKED_FOR_PASSWORD_PROTECTION = "has-asked-for-password-protection";
            public const string DISABLE_TRAY_ICON_LOGIN = "disable-tray-icon-login";
            public const string SERVER_ALLOWED_HOSTNAMES = "allowed-hostnames";
		}

        private readonly Dictionary<string, string> settings;
        private readonly Connection databaseConnection;
        private Library.AutoUpdater.UpdateInfo m_latestUpdate;

        internal ServerSettings(Connection con)
        {
            settings = new Dictionary<string, string>();
            databaseConnection = con;
            ReloadSettings();
        }

        public void ReloadSettings()
        {
            lock(databaseConnection.m_lock)
            {
                settings.Clear();
                foreach(var n in typeof(CONST).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Static).Select(x => (string)x.GetValue(null)))
                    settings[n] = null;
                foreach(var n in databaseConnection.GetSettings(Connection.SERVER_SETTINGS_ID))
                    settings[n.Name] = n.Value;
            }
        }

        public void UpdateSettings(Dictionary<string, string> newsettings, bool clearExisting)
        {
            if (newsettings == null)
                throw new ArgumentNullException();

            lock(databaseConnection.m_lock)
            {
                m_latestUpdate = null;
                if (clearExisting)
                    settings.Clear();

                foreach(var k in newsettings)
                    if (!clearExisting && newsettings[k.Key] == null && k.Key.StartsWith("--", StringComparison.Ordinal))
                        settings.Remove(k.Key);
                    else
                        settings[k.Key] = newsettings[k.Key];

            }

            SaveSettings();
            
            if (newsettings.Keys.Contains(CONST.SERVER_PASSPHRASE))
                GenerateWebserverPasswordTrayIcon();
        }
            
        private void SaveSettings()
        {
            databaseConnection.SetSettings(
                from n in settings
                select (Duplicati.Server.Serialization.Interface.ISetting)new Setting() {
                    Filter = "",
                    Name = n.Key,
                    Value = n.Value
            }, Database.Connection.SERVER_SETTINGS_ID);

			System.Threading.Interlocked.Increment(ref Program.LastDataUpdateID);
			Program.StatusEventNotifyer.SignalNewEvent();

			// In case the usage reporter is enabled or disabled, refresh now
			Program.StartOrStopUsageReporter();
            // If throttle options were changed, update now
            Program.UpdateThrottleSpeeds();
        }
        
        public string StartupDelayDuration
        {
            get 
            {
                return settings[CONST.STARTUP_DELAY];
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.STARTUP_DELAY] = value;
                SaveSettings();
            }
        }
        
        public System.Threading.ThreadPriority? ThreadPriorityOverride
        {
            get
            {
                var tp = settings[CONST.THREAD_PRIORITY];
                if (string.IsNullOrEmpty(tp))
                    return null;
                  
                System.Threading.ThreadPriority r;  
                if (Enum.TryParse<System.Threading.ThreadPriority>(tp, true, out r))
                    return r;
                
                return null;
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.THREAD_PRIORITY] = value.HasValue ? Enum.GetName(typeof(System.Threading.ThreadPriority), value.Value) : null;
            }
        }
        
        public string DownloadSpeedLimit
        {
            get 
            {
                return settings[CONST.DOWNLOAD_SPEED_LIMIT];
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.DOWNLOAD_SPEED_LIMIT] = value;
                SaveSettings();
            }
        }
        
        public string UploadSpeedLimit
        {
            get 
            {
                return settings[CONST.UPLOAD_SPEED_LIMIT];
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.UPLOAD_SPEED_LIMIT] = value;
                SaveSettings();
            }
        }

        public bool IsFirstRun
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBoolOption(settings, CONST.IS_FIRST_RUN);
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.IS_FIRST_RUN] = value.ToString();
                SaveSettings();
            }
        }

        public bool HasAskedForPasswordProtection
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBoolOption(settings, CONST.HAS_ASKED_FOR_PASSWORD_PROTECTION);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.HAS_ASKED_FOR_PASSWORD_PROTECTION] = value.ToString();
                SaveSettings();
            }
        }

        public bool UnackedError
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.UNACKED_ERROR], false);
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.UNACKED_ERROR] = value.ToString();
                SaveSettings();
            }
        }

        public bool UnackedWarning
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.UNACKED_WARNING], false);
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.UNACKED_WARNING] = value.ToString();
                SaveSettings();
            }
        }

        public bool ServerPortChanged
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.SERVER_PORT_CHANGED], false);
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.SERVER_PORT_CHANGED] = value.ToString();
                SaveSettings();
            }
        }

        public bool DisableTrayIconLogin
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.DISABLE_TRAY_ICON_LOGIN], false);
            }
            set
            {
                lock (databaseConnection.m_lock)
                    settings[CONST.DISABLE_TRAY_ICON_LOGIN] = value.ToString();
                SaveSettings();
            }
        }

        public int LastWebserverPort
        {
            get
            {
                var tp = settings[CONST.LAST_WEBSERVER_PORT];
                int p;
                if (string.IsNullOrEmpty(tp) || !int.TryParse(tp, out p))
                    return -1;

                return p;
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.LAST_WEBSERVER_PORT] = value.ToString();
                SaveSettings();
            }
        }

        public string WebserverPassword
        {
            get 
            {
                return settings[CONST.SERVER_PASSPHRASE];
            }
        }

        public string WebserverPasswordSalt
        {
            get 
            {
                return settings[CONST.SERVER_PASSPHRASE_SALT];
            }
        }

        public void SetWebserverPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                lock(databaseConnection.m_lock)
                {
                    settings[CONST.SERVER_PASSPHRASE] = "";
                    settings[CONST.SERVER_PASSPHRASE_SALT] = "";
                }
            }
            else
            {
                var prng = RandomNumberGenerator.Create();
                var buf = new byte[32];
                prng.GetBytes(buf);
                var salt = Convert.ToBase64String(buf);

                var sha256 = System.Security.Cryptography.SHA256.Create();
                var str = System.Text.Encoding.UTF8.GetBytes(password);

                sha256.TransformBlock(str, 0, str.Length, str, 0);
                sha256.TransformFinalBlock(buf, 0, buf.Length);
                var pwd = Convert.ToBase64String(sha256.Hash);

                lock(databaseConnection.m_lock)
                {
                    settings[CONST.SERVER_PASSPHRASE] = pwd;
                    settings[CONST.SERVER_PASSPHRASE_SALT] = salt;
                }
            }

            SaveSettings();
        }

        public void SetAllowedHostnames(string allowedHostnames)
        {
            lock (databaseConnection.m_lock)
                settings[CONST.SERVER_ALLOWED_HOSTNAMES] = allowedHostnames;

            SaveSettings();
        }

        public string WebserverPasswordTrayIcon => settings[CONST.SERVER_PASSPHRASETRAYICON];

        public string WebserverPasswordTrayIconHash => settings[CONST.SERVER_PASSPHRASETRAYICONHASH];

        public string AllowedHostnames => settings[CONST.SERVER_ALLOWED_HOSTNAMES];

        public void GenerateWebserverPasswordTrayIcon()
        {
            var password = "";
            var pwd = "";

            if (!string.IsNullOrEmpty(settings[CONST.SERVER_PASSPHRASE]))
            {
                password = Guid.NewGuid().ToString();
                var buf = Convert.FromBase64String(settings[CONST.SERVER_PASSPHRASE_SALT]);

                var sha256 = System.Security.Cryptography.SHA256.Create();
                var str = System.Text.Encoding.UTF8.GetBytes(password);

                sha256.TransformBlock(str, 0, str.Length, str, 0);
                sha256.TransformFinalBlock(buf, 0, buf.Length);
                pwd = Convert.ToBase64String(sha256.Hash);
            }
            
            lock (databaseConnection.m_lock)
            {
                settings[CONST.SERVER_PASSPHRASETRAYICON] = password;
                settings[CONST.SERVER_PASSPHRASETRAYICONHASH] = pwd;
            }

            SaveSettings();
        }

        public DateTime LastUpdateCheck
        {
            get 
            {
                long t;
                if (long.TryParse(settings[CONST.UPDATE_CHECK_LAST], out t))
                    return new DateTime(t, DateTimeKind.Utc);
                else
                    return new DateTime(0, DateTimeKind.Utc);
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.UPDATE_CHECK_LAST] = value.ToUniversalTime().Ticks.ToString();
                SaveSettings();
            }
        }

        public string UpdateCheckInterval
        {
            get
            {
                var tp = settings[CONST.UPDATE_CHECK_INTERVAL];
                if (string.IsNullOrWhiteSpace(tp))
                    tp = "1W";

                return tp;
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.UPDATE_CHECK_INTERVAL] = value;
                SaveSettings();
                Program.UpdatePoller.Reschedule();
            }
        }

        public DateTime NextUpdateCheck
        {
            get
            {
                try
                {
                    return Duplicati.Library.Utility.Timeparser.ParseTimeInterval(UpdateCheckInterval, LastUpdateCheck);
                }
                catch
                {
                    return LastUpdateCheck.AddDays(7);
                }
            }
        }

        public Library.AutoUpdater.UpdateInfo UpdatedVersion
        {
            get
            {
                if (string.IsNullOrWhiteSpace(settings[CONST.UPDATE_CHECK_NEW_VERSION]))
                    return null;

                try
                {
                    if (m_latestUpdate != null)
                        return m_latestUpdate;

                    using(var tr = new System.IO.StringReader(settings[CONST.UPDATE_CHECK_NEW_VERSION]))
                        return m_latestUpdate = Server.Serialization.Serializer.Deserialize<Library.AutoUpdater.UpdateInfo>(tr);
                }
                catch
                {
                }

                return null;
            }
            set
            {
                string result = null;
                if (value != null)
                {
                    var sb = new System.Text.StringBuilder();
                    using(var tw = new System.IO.StringWriter(sb))
                        Server.Serialization.Serializer.SerializeJson(tw, value);

                    result = sb.ToString();
                }

                m_latestUpdate = value;
                lock(databaseConnection.m_lock)
                    settings[CONST.UPDATE_CHECK_NEW_VERSION] = result;

                SaveSettings();
            }
        }

        public string ServerListenInterface
        {
            get 
            {
                return settings[CONST.SERVER_LISTEN_INTERFACE];
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.SERVER_LISTEN_INTERFACE] = value;
                SaveSettings();
            }
        }

        public X509Certificate2 ServerSSLCertificate
        {
            get
            {
                if (String.IsNullOrEmpty(settings[CONST.SERVER_SSL_CERTIFICATE]))
                    return null;

                if (Platform.IsClientWindows)
                    return new X509Certificate2(Convert.FromBase64String(settings[CONST.SERVER_SSL_CERTIFICATE]));
                else
                    return new X509Certificate2(Convert.FromBase64String(settings[CONST.SERVER_SSL_CERTIFICATE]), "");
            }
            set
            {
                if (value == null)
                {
                    lock (databaseConnection.m_lock)
                        settings[CONST.SERVER_SSL_CERTIFICATE] = String.Empty;
                }
                else
                {
                    if (Platform.IsClientWindows)
                        lock (databaseConnection.m_lock)
                            settings[CONST.SERVER_SSL_CERTIFICATE] = Convert.ToBase64String(value.Export(X509ContentType.Pkcs12));
                    else
                        lock (databaseConnection.m_lock)
                            settings[CONST.SERVER_SSL_CERTIFICATE] = Convert.ToBase64String(value.Export(X509ContentType.Pkcs12, ""));
                }
                SaveSettings();
            }
        }

        public bool FixedInvalidBackupId
        {
            get
            {
                return Duplicati.Library.Utility.Utility.ParseBool(settings[CONST.HAS_FIXED_INVALID_BACKUPID], false);
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.HAS_FIXED_INVALID_BACKUPID] = value.ToString();
                SaveSettings();
            }
        }

        public string UpdateChannel
        {
            get 
            {
                return settings[CONST.UPDATE_CHANNEL];
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.UPDATE_CHANNEL] = value;
                SaveSettings();
            }
        }

        public string UsageReporterLevel
        {
            get 
            {
                return settings[CONST.USAGE_REPORTER_LEVEL];
            }
            set
            {
                lock(databaseConnection.m_lock)
                    settings[CONST.USAGE_REPORTER_LEVEL] = value;
                SaveSettings();
            }
        }
    }
}

