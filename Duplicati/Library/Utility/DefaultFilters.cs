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
using System.Linq;

namespace Duplicati.Library.Utility
{
    [Flags]
    public enum DefaultFilterSet
    {
        None = 0,
        Windows = 1,
        OSX = 2,
        Linux = 4,
        All = Windows | OSX | Linux,
    }

    /// <summary>
    /// This class defines a set of common filters for files that don't typically need to be backed up.
    /// These filters are largely based on the filters described here:
    /// https://superuser.com/questions/443890/what-files-file-types-and-folders-to-exclude-from-user-folder-backup
    /// </summary>
    public static class DefaultFilters
    {
        /// <summary>
        /// Case insensitive mapping from strings to filter sets
        /// /// </summary>
        private static readonly Dictionary<string, DefaultFilterSet> defaultFilterSetMapping =
            ((DefaultFilterSet[])Enum.GetValues(typeof(DefaultFilterSet)))
            .ToDictionary(val => val.ToString(), val => val, StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Filters common for all operating systems
        /// </summary>
        public static IEnumerable<string> Common
        {
            get
            {
                return DefaultFilters.CreateCommonFilters();
            }
        }

        /// <summary>
        /// Filters for Windows
        /// </summary>
        public static IEnumerable<string> Windows
        {
            get
            {
                return DefaultFilters.CreateWindowsFilters();
            }
        }

        /// <summary>
        /// Filters for OSX
        /// </summary>
        public static IEnumerable<string> OSX
        {
            get
            {
                return DefaultFilters.CreateOSXFilters();
            }
        }

        /// <summary>
        /// Filters for Linux
        /// </summary>
        public static IEnumerable<string> Linux
        {
            get
            {
                return DefaultFilters.CreateLinuxFilters();
            }
        }
        
        /// <summary>
        /// Gets the filters indicated by the given filter sets
        /// </summary>
        /// <param name="options">Filter set options</param>
        /// <returns>Default filters</returns>
        public static IEnumerable<FilterExpression> GetFilters(string options)
        {
            bool anyOptions = false;
            DefaultFilterSet filterSets = DefaultFilterSet.None;
            foreach (string option in options.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                anyOptions = true;
                DefaultFilterSet filter;
                if (DefaultFilters.defaultFilterSetMapping.TryGetValue(option, out filter))
                {
                    filterSets |= filter;
                }
                else
                {
                    throw new ArgumentException(Strings.Filters.UnknownDefaultFilterSet(option));
                }
            }

            // If no filter sets are specified, we use the default for the platform we're on
            if (!anyOptions)
            {
                if (Utility.IsClientWindows)
                {
                    filterSets |= DefaultFilterSet.Windows;
                }

                if (Utility.IsClientOSX)
                {
                    filterSets |= DefaultFilterSet.OSX;
                }

                if (Utility.IsClientLinux)
                {
                    filterSets |= DefaultFilterSet.Linux;
                }
            }

            if (filterSets == DefaultFilterSet.None)
            {
                return Enumerable.Empty<FilterExpression>();
            }

            IEnumerable<string> filters = DefaultFilters.Common;

            if ((filterSets & DefaultFilterSet.Windows) == DefaultFilterSet.Windows)
            {
                filters = filters.Concat(DefaultFilters.Windows);
            }

            if ((filterSets & DefaultFilterSet.OSX) == DefaultFilterSet.OSX)
            {
                filters = filters.Concat(DefaultFilters.OSX);
            }

            if ((filterSets & DefaultFilterSet.Linux) == DefaultFilterSet.Linux)
            {
                filters = filters.Concat(DefaultFilters.Linux);
            }

            // Filter down to distinct filters, then convert them to filter expressions
            return filters.Distinct(Utility.ClientFilenameStringComparer).Select(filter => new FilterExpression(filter, false));
        }

        /// <summary>
        /// Creates common filters
        /// </summary>
        /// <returns>Common filters</returns>
        private static string[] CreateCommonFilters()
        {
            return new[]
            {
                DefaultFilters.CreateWildcardFilter(@"*/Google/Chrome/*cache*"),
                DefaultFilters.CreateWildcardFilter(@"*/Google/Chrome/Safe Browsing*"),
                DefaultFilters.CreateWildcardFilter(@"*/iPhoto Library/iPod Photo Cache/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Mozilla/Firefox/*cache*"),
                DefaultFilters.CreateRegexFilter(@".*/(cookies|permissions).sqllite(-.{3})?"),
            };
        }

        /// <summary>
        /// Creates Windows filters
        /// </summary>
        /// <returns>Windows filters</returns>
        private static string[] CreateWindowsFilters()
        {
            var filters = new[]
            {
                DefaultFilters.CreateWildcardFilter(@"*/config.msi/*.rbf"), // https://github.com/duplicati/duplicati/issues/2886
                DefaultFilters.CreateWildcardFilter(@"*.tmp"),
                DefaultFilters.CreateWildcardFilter(@"*.tmp/*"),
                DefaultFilters.CreateWildcardFilter(@"*/$RECYCLE.BIN/*"),
                DefaultFilters.CreateWildcardFilter(@"*/AppData/Apple Computer/Mobile Sync/*"),
                DefaultFilters.CreateWildcardFilter(@"*/AppData/Local/Microsoft/Windows Store/*"),
                DefaultFilters.CreateWildcardFilter(@"*/AppData/Local/Packages/*"), // https://superuser.com/questions/490925/explain-windows-8-windows-store-appdata-packages-and-what-to-backup
                DefaultFilters.CreateWildcardFilter(@"*/AppData/Local/Temp*"),
                DefaultFilters.CreateWildcardFilter(@"*/AppData/LocalLow/*"),
                DefaultFilters.CreateWildcardFilter(@"*/AppData/Temp*"),
                DefaultFilters.CreateWildcardFilter(@"*/Application Data/Apple Computer/Mobile Sync/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Application Data/Application Data*"),
                DefaultFilters.CreateWildcardFilter(@"*/Cookies/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Google/Chrome/User Data/Default/Cookies"),
                DefaultFilters.CreateWildcardFilter(@"*/Google/Chrome/User Data/Default/Cookies-journal"),
                DefaultFilters.CreateWildcardFilter(@"*/I386*"),
                DefaultFilters.CreateWildcardFilter(@"*/Internet Explorer/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Local Settings/History/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Local Settings/Temp*"),
                DefaultFilters.CreateWildcardFilter(@"*/LocalService/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Microsoft*/RecoveryStore*"),
                DefaultFilters.CreateWildcardFilter(@"*/Microsoft*/Windows/*.edb"),
                DefaultFilters.CreateWildcardFilter(@"*/Microsoft*/Windows/*.log"),
                DefaultFilters.CreateWildcardFilter(@"*/Microsoft*/Windows/Cookies*"),
                DefaultFilters.CreateWildcardFilter(@"*/MSOCache*"),
                DefaultFilters.CreateWildcardFilter(@"*/NetHood/*"),
                DefaultFilters.CreateWildcardFilter(@"*/NetworkService/*"),
                DefaultFilters.CreateWildcardFilter(@"*/NTUSER*"),
                DefaultFilters.CreateWildcardFilter(@"*/ntuser.dat*"),
                DefaultFilters.CreateWildcardFilter(@"*/PrintHood/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Recent/*"),
                DefaultFilters.CreateWildcardFilter(@"*/RECYCLER/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Safari/Library/Caches/*"),
                DefaultFilters.CreateWildcardFilter(@"*/SendTo/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Start Menu/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Temporary Internet Files/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Thumbs.db"),
                DefaultFilters.CreateWildcardFilter(@"*UsrClass.dat"),
                DefaultFilters.CreateWildcardFilter(@"*UsrClass.dat.LOG"),
                DefaultFilters.CreateWildcardFilter(@"?:/autoexec.bat"),
                DefaultFilters.CreateWildcardFilter(@"?:/Config.Msi*"),
                DefaultFilters.CreateWildcardFilter(@"?:/hiberfil.sys"),
                DefaultFilters.CreateWildcardFilter(@"?:/pagefile.sys"),
                DefaultFilters.CreateWildcardFilter(@"?:/Program Files (x86)/*"),
                DefaultFilters.CreateWildcardFilter(@"?:/Program Files/*"),
                DefaultFilters.CreateWildcardFilter(@"?:/ProgramData/*"),
                DefaultFilters.CreateWildcardFilter(@"?:/swapfile.sys"),
                DefaultFilters.CreateWildcardFilter(@"?:/System Volume Information/*"),
                DefaultFilters.CreateWildcardFilter(@"?:/Windows.old/*"),
                DefaultFilters.CreateWildcardFilter(@"?:/Windows/*"),
                DefaultFilters.CreateWildcardFilter(@"?:/Windows/Installer*"),
                DefaultFilters.CreateWildcardFilter(@"?:/Windows/Temp*"),
            };

            var extra = GetWindowsRegistryFilters();
            if (extra == null || extra.Length == 0)
                return filters;

            return filters.Union(extra).ToArray();
        }

        /// <summary>
        /// Creates OSX filters
        /// </summary>
        /// <returns>OSX filters</returns>
        private static string[] CreateOSXFilters()
        {
            return new[]
            {
                DefaultFilters.CreateWildcardFilter(@"*.fseventsd*"),
                DefaultFilters.CreateWildcardFilter(@"*.hotfiles.btree*"),
                DefaultFilters.CreateWildcardFilter(@"*.Spotlight-*/*"),
                DefaultFilters.CreateWildcardFilter(@"*.Trash*"),
                DefaultFilters.CreateWildcardFilter(@"*/Application Support/Google/Chrome/Default/Cookies"),
                DefaultFilters.CreateWildcardFilter(@"*/Application Support/Google/Chrome/Default/Cookies-journal"),
                DefaultFilters.CreateWildcardFilter(@"*/backups.backupdb/*"),
                DefaultFilters.CreateWildcardFilter(@"*/iP* Software Updates/*"),
                DefaultFilters.CreateWildcardFilter(@"*/iPhoto Library/iPod Photo Cache*"),
                DefaultFilters.CreateWildcardFilter(@"*/iTunes/Album Artwork/Cache/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Application Support/SyncServices/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Caches/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Calendars/*/Info.plist"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Calendars/Calendar Cache"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Cookies/com.apple.appstore.plist"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Cookies/Cookies.binarycookies"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Logs/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Mail/*/Info.plist"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Mail/AvailableFeeds/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Mail/Envelope Index"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Mirrors/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/PubSub/Database/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/PubSub/Downloads/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/PubSub/Feeds/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Safari/HistoryIndex.sk"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Safari/Icons.db"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Safari/WebpageIcons.db"),
                DefaultFilters.CreateWildcardFilter(@"*/Library/Saved Application State/*"),
                DefaultFilters.CreateWildcardFilter(@"*/lost+found/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Microsoft User Data/Entourage Temp/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Network Trash Folder/*"),
                DefaultFilters.CreateWildcardFilter(@"*/Trash/*"),
                DefaultFilters.CreateWildcardFilter(@"*/VM Storage"),
                DefaultFilters.CreateWildcardFilter(@"*Mobile.*Backups/*"),
                DefaultFilters.CreateWildcardFilter(@"/.vol/*"),
                DefaultFilters.CreateWildcardFilter(@"/afs/*"),
                DefaultFilters.CreateWildcardFilter(@"/automount/*"),
                DefaultFilters.CreateWildcardFilter(@"/bin/*"),
                DefaultFilters.CreateWildcardFilter(@"/cores/*"),
                DefaultFilters.CreateWildcardFilter(@"/Desktop DB"),
                DefaultFilters.CreateWildcardFilter(@"/Desktop DF"),
                DefaultFilters.CreateWildcardFilter(@"/dev/.*"),
                DefaultFilters.CreateWildcardFilter(@"/etc/*"),
                DefaultFilters.CreateWildcardFilter(@"/mach.sym"),
                DefaultFilters.CreateWildcardFilter(@"/mach_kernel"),
                DefaultFilters.CreateWildcardFilter(@"/net/*"),
                DefaultFilters.CreateWildcardFilter(@"/Network/*"),
                DefaultFilters.CreateWildcardFilter(@"/Network/Servers*"),
                DefaultFilters.CreateWildcardFilter(@"/Previous Systems*"),
                DefaultFilters.CreateWildcardFilter(@"/private/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/Network/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/tmp/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/var/automount/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/var/db/dhcpclient/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/var/db/fseventsd/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/var/folders/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/var/run/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/var/spool/postfix/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/var/tmp/*"),
                DefaultFilters.CreateWildcardFilter(@"/private/var/vm/*"),
                DefaultFilters.CreateWildcardFilter(@"/sbin/*"),
                DefaultFilters.CreateWildcardFilter(@"/sw/*"),
                DefaultFilters.CreateWildcardFilter(@"/System/*"),
                DefaultFilters.CreateWildcardFilter(@"/System/Library/Extensions/Caches/*"),
                DefaultFilters.CreateWildcardFilter(@"/tmp/*"),
                DefaultFilters.CreateWildcardFilter(@"/Users/Shared/SC Info*"),
                DefaultFilters.CreateWildcardFilter(@"/usr/*"),
            };
        }

        /// <summary>
        /// Creates Linux filters
        /// </summary>
        /// <returns>Linux filters</returns>
        private static string[] CreateLinuxFilters()
        {
            return new[]
            {
                DefaultFilters.CreateWildcardFilter(@"*/.config/google-chrome/Default/Cookies"),
                DefaultFilters.CreateWildcardFilter(@"*/.config/google-chrome/Default/Cookies-journal"),
                DefaultFilters.CreateWildcardFilter(@"*/lost+found/*"),
                DefaultFilters.CreateWildcardFilter(@"*~"),
                DefaultFilters.CreateWildcardFilter(@"/bin/*"),
                DefaultFilters.CreateWildcardFilter(@"/boot/*"),
                DefaultFilters.CreateWildcardFilter(@"/dev/*"),
                DefaultFilters.CreateWildcardFilter(@"/etc/*"),
                DefaultFilters.CreateWildcardFilter(@"/initrd/*"),
                DefaultFilters.CreateWildcardFilter(@"/lib/*"),
                DefaultFilters.CreateWildcardFilter(@"/opt/*"),
                DefaultFilters.CreateWildcardFilter(@"/proc/*"),
                DefaultFilters.CreateWildcardFilter(@"/sbin/*"),
                DefaultFilters.CreateWildcardFilter(@"/selinux/*"),
                DefaultFilters.CreateWildcardFilter(@"/srv/*"),
                DefaultFilters.CreateWildcardFilter(@"/sys/*"),
                DefaultFilters.CreateWildcardFilter(@"/tmp/*"),
                DefaultFilters.CreateWildcardFilter(@"/usr/*"),
                DefaultFilters.CreateWildcardFilter(@"/var/*"),
            };
        }

        /// <summary>
        /// Creates a wildcard filter
        /// </summary>
        /// <param name="filter">Filter text</param>
        /// <returns>Wildcard filter</returns>
        private static string CreateWildcardFilter(string filter)
        {
            // Create a filter with the given name.
            // However, in order to match paths correctly, the directory separators need to be normalized to match the system default.
            return filter.Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Creates a Regex filter
        /// </summary>
        /// <param name="filter">Filter text</param>
        /// <returns>Regex filter</returns>
        private static string CreateRegexFilter(string filter)
        {
            // Create a filter with the given name.
            // However, in order to match paths correctly, the directory separators need to be normalized to match the system default.
            string escapedAlt = System.Text.RegularExpressions.Regex.Escape(System.IO.Path.AltDirectorySeparatorChar.ToString());
            string escaped = System.Text.RegularExpressions.Regex.Escape(System.IO.Path.DirectorySeparatorChar.ToString());
            return "[" + filter.Replace(escapedAlt, escaped) + "]";
        }

        /// <summary>
        /// Wrapper method for getting a list of exclude paths from the Windows registry.
        /// This method guards the call to <see cref="GetWindowsRegistryFiltersInternal"/> to avoid loader errors.
        /// </summary>
        /// <returns>The list of paths to exclude.</returns>
        private static string[] GetWindowsRegistryFilters()
        {
            if (Utility.IsClientWindows)
            {
                // One Windows, filters may also be stored in the registry
                try
                {
                    return GetWindowsRegistryFiltersInternal();
                }
                catch
                {
                }
            }

            return null;
        }

        /// <summary>
        /// Helper method that reads the Windows registry and finds paths to exclude.
        /// This method should not be called directly as that could cause loader errors on Mono.
        /// </summary>
        /// <returns>The list of paths to exclude.</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string[] GetWindowsRegistryFiltersInternal()
        {
            var rk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Default);
            if (rk == null)
                return new string[0];

            var sk = rk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\BackupRestore\FilesNotToBackup");
            if (sk == null)
                return new string[0];

            // Each value in this key is a potential path
            return sk.GetValueNames()
                 .Where(x => x != null)
                 .SelectMany(x =>
                 {
                     var v = sk.GetValue(x);
                     if (v is string)
                         return new string[] { (string)v };
                     else if (v is string[])
                         return (string[])v;
                     else
                         return new string[0];
                 })
                 .Where(x => !string.IsNullOrWhiteSpace(x))
                 .Select(x => Environment.ExpandEnvironmentVariables(x))
                 .Where(x => !string.IsNullOrWhiteSpace(x))
                 .Where(x => x.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0)
                 .Where(x => System.IO.Path.IsPathRooted(x))
                 .Select(x => x.EndsWith(" /s", StringComparison.OrdinalIgnoreCase) ? x.Substring(0, x.Length - 3).TrimEnd() : x)
                 .ToArray();
        }

    }
}
