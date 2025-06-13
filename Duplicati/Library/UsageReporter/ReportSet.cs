// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using Newtonsoft.Json;
using System.Collections.Generic;

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
        private static string Cached_PackageTypeId;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoInitUID()
        {
            Cached_UserID = Library.AutoUpdater.DataFolderManager.InstallID;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoInitOS()
        {
            if (OperatingSystem.IsMacOS())
                Cached_OSType = "MacOS";
            else if (OperatingSystem.IsLinux())
                Cached_OSType = "Linux";
            else if (OperatingSystem.IsWindows())
                Cached_OSType = "Windows";
            else
                Cached_OSType = "Unknown";

            Cached_OSVersion = OSInfoHelper.PlatformString;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoInitPackageType()
        {
            Cached_PackageTypeId = Library.AutoUpdater.UpdaterManager.PackageTypeId;
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

            try { DoInitPackageType(); }
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
        [JsonProperty("pkgid")]
        public string PackageTypeId { get; set; }
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
            this.PackageTypeId = Cached_PackageTypeId;
            this.AppName = Cached_AppName;
            this.AppVersion = Cached_AppVersion;
            this.Assembly = Cached_Assembly;
            this.SetID = Guid.NewGuid().ToString("N");
            this.Items = new List<ReportItem>();
        }
    }
}

