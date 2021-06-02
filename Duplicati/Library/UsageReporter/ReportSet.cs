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
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Common;

namespace Duplicati.Library.UsageReporter
{
    /// <summary>
    /// An envelope type for sending multiple
    /// items together to the server
    /// </summary>
    internal class ReportSet
    {
        private static string Cached_UserID;
        private static string Cached_OSType;
        private static string Cached_OSVersion;
        private static string Cached_CLRVersion;
        private static string Cached_AppName;
        private static string Cached_AppVersion;
        private static string Cached_Assembly;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoInitUID()
        {
            Cached_UserID = Library.AutoUpdater.UpdaterManager.InstallID;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoInitOS()
        {
            if (Platform.IsClientOSX)
                Cached_OSType = "OSX";
            else if (Platform.IsClientPosix)
                Cached_OSType = "Linux";
            else if (new PlatformID[] {
                PlatformID.Win32NT,
                PlatformID.Win32S,
                PlatformID.Win32Windows
            }.Contains(Environment.OSVersion.Platform))
                Cached_OSType = "Windows";
            else
                Cached_OSType = Environment.OSVersion.Platform.ToString();

            Cached_OSVersion = OSInfoHelper.PlatformString;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoInitCLR()
        {
            Cached_CLRVersion = string.Format(".Net {0}", Environment.Version);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoInitApp()
        {
            Cached_AppName = AutoUpdater.AutoUpdateSettings.AppName;
            Cached_AppVersion = AutoUpdater.UpdaterManager.SelfVersion.Version;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoInitAssembly()
        {
            Cached_Assembly = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
        }


        static ReportSet()
        {
            // Keep it here to avoid crashing
            // if the loader fails to grab an ID
            try { DoInitUID(); }
            catch { }

            try { DoInitOS(); }
            catch { }

            try { DoInitCLR(); }
            catch { }

            try { DoInitApp(); }
            catch { }

            try { DoInitAssembly(); }
            catch { }
        }


        [JsonProperty("uid")]
        public string ReporterID { get; set; }
        [JsonProperty("ostype")]
        public string OSType { get; set; }
        [JsonProperty("osversion")]
        public string OSVersion { get; set; }
        [JsonProperty("clrversion")]
        public string CLRVersion { get; set; }
        [JsonProperty("appname")]
        public string AppName { get; set; }
        [JsonProperty("appversion")]
        public string AppVersion { get; set; }
        [JsonProperty("setid")]
        public string SetID { get; set; }
        [JsonProperty("assembly")]
        public string Assembly { get; set; }
        [JsonProperty("items")]
        public List<ReportItem> Items { get; set; }

        public ReportSet()
        {
            this.ReporterID = Cached_UserID;
            this.OSType = Cached_OSType;
            this.OSVersion = Cached_OSVersion;
            this.CLRVersion = Cached_CLRVersion;
            this.AppName = Cached_AppName;
            this.AppVersion = Cached_AppVersion;
            this.Assembly = Cached_Assembly;
            this.SetID = Guid.NewGuid().ToString("N");
            this.Items = new List<ReportItem>();
        }
    }
}

