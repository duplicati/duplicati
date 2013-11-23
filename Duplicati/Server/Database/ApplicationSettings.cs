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
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Server.Database
{
    public class ApplicationSettings
    {
        private class CONST
        {
            public const string STARTUP_DELAY = "startup-delay";
            public const string DOWNLOAD_SPEED_LIMIT = "max-download-speed";
            public const string UPLOAD_SPEED_LIMIT = "max-upload-speed";
            public const string THREAD_PRIORITY = "thread-priority";
        }
        
        private Dictionary<string, string> m_values;
    
        internal ApplicationSettings(Connection con)
        {
            var settings = con.GetSettings(Connection.APP_SETTINGS_ID).ToDictionary(x => x.Name, x => x.Value);
            m_values = new Dictionary<string, string>();
            
            string nx;
            foreach(var n in typeof(CONST).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Static).Select(x => (string)x.GetValue(null)))
            {
                settings.TryGetValue(n, out nx);
                m_values[n] = nx;
            }
        }
        
        private void SaveSettings()
        {
            Program.DataConnection.SetSettings(
                from n in m_values
                select (Duplicati.Server.Serialization.Interface.ISetting)new Setting() {
                    Filter = "",
                    Name = n.Key,
                    Value = n.Value
                }, Database.Connection.APP_SETTINGS_ID);
        }
        
        public string StartupDelayDuration
        {
            get 
            {
                return m_values[CONST.STARTUP_DELAY];
            }
            set
            {
                m_values[CONST.STARTUP_DELAY] = value;
                SaveSettings();
            }
        }
        
        public System.Threading.ThreadPriority? ThreadPriorityOverride
        {
            get
            {
                var tp = m_values[CONST.THREAD_PRIORITY];
                if (string.IsNullOrEmpty(tp))
                    return null;
                  
                System.Threading.ThreadPriority r;  
                if (Enum.TryParse<System.Threading.ThreadPriority>(tp, true, out r))
                    return r;
                
                return null;
            }
            set
            {
                m_values[CONST.THREAD_PRIORITY] = value.HasValue ? Enum.GetName(typeof(System.Threading.ThreadPriority), value.Value) : null;
            }
        }
        
        public string DownloadSpeedLimit
        {
            get 
            {
                return m_values[CONST.DOWNLOAD_SPEED_LIMIT];
            }
            set
            {
                m_values[CONST.DOWNLOAD_SPEED_LIMIT] = value;
                SaveSettings();
            }
        }
        
        public string UploadSpeedLimit
        {
            get 
            {
                return m_values[CONST.UPLOAD_SPEED_LIMIT];
            }
            set
            {
                m_values[CONST.UPLOAD_SPEED_LIMIT] = value;
                SaveSettings();
            }
        }
    }
}

