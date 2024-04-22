// Copyright (C) 2024, The Duplicati Team
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
using System.Collections.Generic;
using Duplicati.Library.Interface;
using System.Linq;
using Duplicati.Library.Common;

namespace Duplicati.Library.Main.Operation
{
    internal static class SystemInfoHandler
    {
        public static void Run(SystemInfoResults results)
        {
            results.Lines = GetSystemInfo().ToArray();
        }

        public static IEnumerable<string> GetSystemInfo()
        {
            yield return string.Format("Duplicati: {0} ({1})", Duplicati.Library.Utility.Utility.getEntryAssembly().FullName, System.Reflection.Assembly.GetExecutingAssembly().FullName);

            yield return string.Format("Autoupdate urls: {0}", string.Join(";", Duplicati.Library.AutoUpdater.AutoUpdateSettings.URLs));
            yield return string.Format("Update folder: {0}", Duplicati.Library.AutoUpdater.UpdaterManager.UPDATEDIR);
            yield return string.Format("Install folder: {0}", Duplicati.Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR);
            yield return string.Format("Version name: \"{0}\" ({1})", Duplicati.Library.AutoUpdater.UpdaterManager.SelfVersion.Displayname, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            yield return string.Format("Current Version folder {0}", System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));

            yield return string.Format("OS: {0}", Environment.OSVersion);
            yield return string.Format("Uname: {0}", Platform.UnameAll);

            yield return string.Format("64bit: {0} ({1})", Environment.Is64BitOperatingSystem, Environment.Is64BitProcess);
            yield return string.Format("Machinename: {0}", Environment.MachineName);
            yield return string.Format("Processors: {0}", Environment.ProcessorCount);
            yield return string.Format(".Net Version: {0}", Environment.Version);
            yield return string.Format("Locale: {0}, {1}, {2}", System.Threading.Thread.CurrentThread.CurrentCulture, System.Threading.Thread.CurrentThread.CurrentUICulture, System.Globalization.CultureInfo.InstalledUICulture);
            yield return string.Format("Date/time strings: {0} - {1}", System.Threading.Thread.CurrentThread.CurrentCulture.DateTimeFormat.LongDatePattern, System.Threading.Thread.CurrentThread.CurrentCulture.DateTimeFormat.LongTimePattern);
            yield return string.Format("Tempdir: {0}", Library.Utility.TempFolder.SystemTempPath);

            Type sqlite = null;
            string sqliteversion = "";

            try { sqlite = Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType; }
            catch { }

            if (sqlite != null)
            {
                try { sqliteversion = Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteVersion; }
                catch { }

                yield return string.Format("SQLite: {0} - {1}", sqliteversion, sqlite.FullName);
                yield return string.Format("SQLite assembly: {0}", sqlite.Assembly.Location);
            }
        }
    }
}

