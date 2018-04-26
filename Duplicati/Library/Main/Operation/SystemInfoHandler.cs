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
using System.Collections.Generic;
using Duplicati.Library.Interface;
using System.Linq;

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
            yield return string.Format("Update folder: {0}", Duplicati.Library.AutoUpdater.UpdaterManager.INSTALLDIR);
            yield return string.Format("Base install folder: {0}", Duplicati.Library.AutoUpdater.UpdaterManager.InstalledBaseDir);
            yield return string.Format("Version name: \"{0}\" ({1})", Duplicati.Library.AutoUpdater.UpdaterManager.SelfVersion.Displayname, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            yield return string.Format("Current Version folder {0}", System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));

            yield return string.Format("OS: {0}", Environment.OSVersion);
            yield return string.Format("Uname: {0}", Duplicati.Library.Utility.Utility.UnameAll);

            yield return string.Format("64bit: {0} ({1})", Environment.Is64BitOperatingSystem, Environment.Is64BitProcess);
            yield return string.Format("Machinename: {0}", Environment.MachineName);
            yield return string.Format("Processors: {0}", Environment.ProcessorCount);
            yield return string.Format(".Net Version: {0}", Environment.Version);
            yield return string.Format("Mono: {0} ({1}) ({2})", Duplicati.Library.Utility.Utility.IsMono, Duplicati.Library.Utility.Utility.MonoVersion, Duplicati.Library.Utility.Utility.MonoDisplayVersion);
            yield return string.Format("Locale: {0}, {1}, {2}", System.Threading.Thread.CurrentThread.CurrentCulture, System.Threading.Thread.CurrentThread.CurrentUICulture, System.Globalization.CultureInfo.InstalledUICulture);
            yield return string.Format("Date/time strings: {0} - {1}", System.Threading.Thread.CurrentThread.CurrentCulture.DateTimeFormat.LongDatePattern, System.Threading.Thread.CurrentThread.CurrentCulture.DateTimeFormat.LongTimePattern);
            yield return string.Format("Tempdir: {0}", Library.Utility.TempFolder.SystemTempPath);
            foreach(var e in new string[] {"TEMP", "TMP", "TMPDIR"})
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(e)))
                    yield return string.Format("Environment variable: {0} = {1}", e, Environment.GetEnvironmentVariable(e));
            
            Type sqlite = null;
            string sqliteversion = "";

            try { sqlite = Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType; }
            catch { }

            if (sqlite != null)
            {
                try { sqliteversion = (string)sqlite.GetProperty("SQLiteVersion").GetValue(null, null); }
                catch { }

                yield return string.Format("SQLite: {0} - {1}", sqliteversion, sqlite.FullName);
                yield return string.Format("SQLite assembly: {0}", sqlite.Assembly.Location);
            }
        }
    }
}

