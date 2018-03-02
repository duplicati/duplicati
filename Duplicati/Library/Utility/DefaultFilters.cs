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
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// The filter groups
    /// </summary>
    [Flags]
    public enum DefaultFilterGroup
    {
        /// <summary>
        /// Placeholder that indicates no filters
        /// </summary>
        None = 0x00,
        /// <summary>
        /// Files and paths that are owned by the operating system or are not real files
        /// </summary>
        SystemFiles = 0x01,
        /// <summary>
        /// Files and folders that are normally part of the operating system
        /// </summary>
        OperatingSystem = 0x02,
        /// <summary>
        /// Files and folders that are known to be cache locations
        /// </summary>
        CacheFiles = 0x04,
        /// <summary>
        /// Files and folders that are temporary locations
        /// </summary>
        TemporaryFolders = 0x08,
        /// <summary>
        /// Files and folders that are application binaries (i.e. installed programs)
        /// </summary>
        Applications = 0x10,

        /// <summary>
        /// Meta option that includes all the other filters
        /// </summary>
        All = SystemFiles | OperatingSystem | CacheFiles | TemporaryFolders | Applications,

        /// <summary>
        /// Meta option that specifies the default filters
        /// </summary>
        Default = All
    }

    /// <summary>
    /// This class defines a set of common filters for files that don't typically need to be backed up.
    /// These filters are largely based on the filters described here:
    /// https://superuser.com/questions/443890/what-files-file-types-and-folders-to-exclude-from-user-folder-backup
    /// which are in turn based on the filters defined by Crashplan:
    /// https://support.code42.com/CrashPlan/4/Troubleshooting/What_is_not_backing_up#Admin_Excludes
    /// </summary>
    public static class DefaultFilters
    {
        /// <summary>
        /// Helper method that parses a string with a number of options and returns the group that it represents
        /// </summary>
        /// <returns>The filters specified by the group.</returns>
        /// <param name="filters">The filter string.</param>
        /// <param name="defaultValue">The value to use if there are no filter groups specified in the string</param>
        public static DefaultFilterGroup ParseFilterList(string filters, DefaultFilterGroup defaultValue = DefaultFilterGroup.Default)
        {
            var res = DefaultFilterGroup.None;
            var any = false;
            foreach (var f in (filters ?? string.Empty).Split(new char[] { ':', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                any = true;
                if (!Enum.TryParse<DefaultFilterGroup>(f, true, out var fg))
                    throw new ArgumentException(Strings.Filters.UnknownFilterGroup(f));

                res |= fg;
            }

            if (!any)
                res = defaultValue;

            return res;
        }

        /// <summary>
        /// Gets a string that describes the default filter options
        /// </summary>
        /// <returns>The option descriptions.</returns>
        /// <param name="indentation">The string indentation to use.</param>
        /// <param name="includevalues">If set to <c>true</c> include actual filter paths in the output.</param>
        public static string GetOptionDescriptions(int indentation, bool includevalues)
        {
            var def = DefaultFilterGroup.Default;
            var defaultvalues = new List<string>();
            foreach (var e in Enum.GetValues(typeof(DefaultFilterGroup)))
            {
                var ed = (DefaultFilterGroup)e;
                if (def.HasFlag(ed) && ed != def && ed != DefaultFilterGroup.All && ed != DefaultFilterGroup.None)
                    defaultvalues.Add(e.ToString());
            }

            var ind = new string(' ', indentation);
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(ind + LC.L("{0}: Selects no filters.", nameof(DefaultFilterGroup.None)));
            sb.AppendLine(ind + LC.L("{0}: Selects all the filter groups.", nameof(DefaultFilterGroup.All)));
            sb.AppendLine(ind + LC.L("{0}: A set of default filters, currently evaluates to: {1}.", nameof(DefaultFilterGroup.Default), string.Join(",", defaultvalues)));

            if (includevalues)
                sb.AppendLine();
            
            sb.AppendLine(ind + LC.L("{0}: Files that are owned by the system or not suited to be backed up. This includes any operating system reported protected files. Most users should at least apply these filters.", Library.Utility.DefaultFilterGroup.SystemFiles));
            if (includevalues)
            {
                foreach (var v in GetFilterStrings(DefaultFilterGroup.SystemFiles))
                    sb.AppendLine(ind + "  " + v);
                sb.AppendLine();
            }
            sb.AppendLine(ind + LC.L("{0}: Files that belong to the operating system. These files are restored when the operating system is re-installed.", Library.Utility.DefaultFilterGroup.OperatingSystem));
            if (includevalues)
            {
                foreach (var v in GetFilterStrings(DefaultFilterGroup.OperatingSystem))
                    sb.AppendLine(ind + "  " + v);
                sb.AppendLine();
            }
            sb.AppendLine(ind + LC.L("{0}: Files and folders that are known to be storage of temporary data.", Library.Utility.DefaultFilterGroup.TemporaryFolders));
            if (includevalues)
            {
                foreach (var v in GetFilterStrings(DefaultFilterGroup.TemporaryFolders))
                    sb.AppendLine(ind + "  " + v);
                sb.AppendLine();
            }
            sb.AppendLine(ind + LC.L("{0}: Files and folders that are known cache locations for the operating system and various applications", Library.Utility.DefaultFilterGroup.CacheFiles));
            if (includevalues)
            {
                foreach (var v in GetFilterStrings(DefaultFilterGroup.CacheFiles))
                    sb.AppendLine(ind + "  " + v);
                sb.AppendLine();
            }
            sb.AppendLine(ind + LC.L("{0}: Installed programs and their libraries, but not their settings.", Library.Utility.DefaultFilterGroup.Applications));
            if (includevalues)
            {
                foreach (var v in GetFilterStrings(DefaultFilterGroup.Applications))
                    sb.AppendLine(ind + "  " + v);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the filters for a specific group
        /// </summary>
        /// <returns>The filters from the group.</returns>
        /// <param name="group">The group to use.</param>
        private static IEnumerable<string> GetFilterStrings(DefaultFilterGroup group)
        {
            IEnumerable<string> osFilters;

            if (Utility.IsClientOSX)
                osFilters = CreateOSXFilters(group);
            else if (Utility.IsClientLinux)
                osFilters = CreateLinuxFilters(group);
            else if (Utility.IsClientWindows)
                osFilters = CreateWindowsFilters(group);
            else
                throw new ArgumentException("Unknown operating system?");

            return
                FilterPrefixMatches(
                    CreateCommonFilters(group)
                        .Concat(osFilters)
                    )
                    .Distinct(Utility.ClientFilenameStringComparer);
        }

        /// <summary>
        /// Gets the filters for a specific group
        /// </summary>
        /// <returns>The filters from the group.</returns>
        /// <param name="groups">The groups to use.</param>
        public static IEnumerable<IFilter> GetFilters(string groups)
        {
            return GetFilters(ParseFilterList(groups));
        }

        /// <summary>
        /// Gets the filters for a specific group
        /// </summary>
        /// <returns>The filters from the group.</returns>
        /// <param name="group">The group to use.</param>
        public static IEnumerable<IFilter> GetFilters(DefaultFilterGroup group)
        {
            return GetFilterStrings(group)
                    .Select(x => new FilterExpression(x, false));
        }

        /// <summary>
        /// Filters all items that have a prefix
        /// </summary>
        /// <returns>The prefix matches.</returns>
        /// <param name="filters">Filters.</param>
        private static IEnumerable<string> FilterPrefixMatches(IEnumerable<string> filters)
        {
            string prev = null;
            foreach (var n in filters.OrderBy(x => x, Utility.ClientFilenameStringComparer))
                if (prev != null && prev.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) && n.StartsWith(prev, Utility.ClientFilenameStringComparision))
                    continue;
                else
                    yield return prev = n;
        }

        /// <summary>
        /// Creates common filters
        /// </summary>
        /// <param name="group">The groups to create the filters for</param>
        /// <returns>Common filters</returns>
        private static IEnumerable<string> CreateCommonFilters(DefaultFilterGroup group)
        {
            if (group.HasFlag(DefaultFilterGroup.CacheFiles))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"*/Google/Chrome/*cache*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Google/Chrome/Safe Browsing*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/iPhoto Library/iPod Photo Cache/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Mozilla/Firefox/*cache*");
                yield return DefaultFilters.CreateRegexFilter(@".*/(cookies|permissions).sqllite(-.{3})?");
            }

            if (group.HasFlag(DefaultFilterGroup.TemporaryFolders))
            {
                yield return Library.Utility.TempFolder.SystemTempPath;
            }
        }

        /// <summary>
        /// Creates Windows filters
        /// </summary>
        /// <param name="group">The groups to create the filters for</param>
        /// <returns>Windows filters</returns>
        private static IEnumerable<string> CreateWindowsFilters(DefaultFilterGroup group)
        {
            if (group.HasFlag(DefaultFilterGroup.SystemFiles))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"*/I386*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Internet Explorer/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Microsoft*/RecoveryStore*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Microsoft*/Windows/*.edb");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Microsoft*/Windows/*.log");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Microsoft*/Windows/Cookies*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/MSOCache*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/NTUSER*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/RECYCLER/");
                yield return DefaultFilters.CreateWildcardFilter(@"*UsrClass.dat");
                yield return DefaultFilters.CreateWildcardFilter(@"*UsrClass.dat.LOG");
                yield return DefaultFilters.CreateWildcardFilter(@"?:/hiberfil.sys");
                yield return DefaultFilters.CreateWildcardFilter(@"?:/pagefile.sys");
                yield return DefaultFilters.CreateWildcardFilter(@"?:/swapfile.sys");
                yield return DefaultFilters.CreateWildcardFilter(@"?:/System Volume Information/");
                yield return DefaultFilters.CreateWildcardFilter(@"?:/Windows/Installer*");
                yield return DefaultFilters.CreateWildcardFilter(@"?:/Windows/Temp*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/ntuser.dat*");

                foreach (var s in GetWindowsRegistryFilters() ?? new string[0])
                    yield return s;
            }

            if (group.HasFlag(DefaultFilterGroup.OperatingSystem))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"?:/Config.Msi*"); // https://github.com/duplicati/duplicati/issues/2886
                yield return DefaultFilters.CreateWildcardFilter(@"*/Recent/");
                yield return DefaultFilters.CreateWildcardFilter(@"?:/autoexec.bat");
                yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.Recent);

                var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrWhiteSpace(windir))
                {
                    yield return windir;

                    // Also exclude "C:\Windows.old\"
                    yield return windir.TrimEnd('\\') + ".old\\";
                }
            }

            if (group.HasFlag(DefaultFilterGroup.CacheFiles))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"*/AppData/Apple Computer/Mobile Sync/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/AppData/Local/Microsoft/Windows Store/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/AppData/Local/Packages/"); // https://superuser.com/questions/490925/explain-windows-8-windows-store-appdata-packages-and-what-to-backup
                yield return DefaultFilters.CreateWildcardFilter(@"*/Application Data/Apple Computer/Mobile Sync/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Application Data/Application Data*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Google/Chrome/User Data/Default/Cookies");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Google/Chrome/User Data/Default/Cookies-journal");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Thumbs.db");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Safari/Library/Caches/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Temporary Internet Files/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Local Settings/History/");

                yield return Environment.GetFolderPath(Environment.SpecialFolder.History);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.Cookies);

            }

            if (group.HasFlag(DefaultFilterGroup.TemporaryFolders))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"*/$RECYCLE.BIN/");
                yield return DefaultFilters.CreateWildcardFilter(@"*.tmp");
                yield return DefaultFilters.CreateWildcardFilter(@"*.tmp/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/AppData/Local/Temp*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/AppData/Temp*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Local Settings/Temp*");
                yield return DefaultFilters.CreateWildcardFilter(@"?:/Windows/Temp*");
            }

            if (group.HasFlag(DefaultFilterGroup.Applications))
            {
                yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.AdminTools);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonAdminTools);
            }
        }

        /// <summary>
        /// Creates OSX filters
        /// </summary>
        /// <param name="group">The groups to create the filters for</param>
        /// <returns>OSX filters</returns>
        private static IEnumerable<string> CreateOSXFilters(DefaultFilterGroup group)
        {
            if (group.HasFlag(DefaultFilterGroup.SystemFiles))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"/.vol/");
                yield return DefaultFilters.CreateWildcardFilter(@"/dev/");
                yield return DefaultFilters.CreateWildcardFilter(@"/net/");
                yield return DefaultFilters.CreateWildcardFilter(@"/afs/");
                yield return DefaultFilters.CreateWildcardFilter(@"/automount/");
                yield return DefaultFilters.CreateWildcardFilter(@"/cores/");
                yield return DefaultFilters.CreateWildcardFilter(@"/Network/");
                yield return DefaultFilters.CreateWildcardFilter(@"*.fseventsd");
                yield return DefaultFilters.CreateWildcardFilter(@"*.dbfseventsd");

                yield return DefaultFilters.CreateWildcardFilter(@"/private/");
                /*
                yield return DefaultFilters.CreateWildcardFilter(@"/private/Network/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/tmp/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/var/automount/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/var/db/dhcpclient/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/var/db/fseventsd/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/var/folders/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/var/run/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/var/spool/postfix/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/var/tmp/");
                yield return DefaultFilters.CreateWildcardFilter(@"/private/var/vm/");
                */

                foreach (var p in GetOSXExcludeFiles() ?? new string[0])
                    yield return p;
            }

            if (group.HasFlag(DefaultFilterGroup.OperatingSystem))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"/Previous Systems*");
                yield return DefaultFilters.CreateWildcardFilter(@"/mach.sym");
                yield return DefaultFilters.CreateWildcardFilter(@"/mach_kernel");
                yield return DefaultFilters.CreateWildcardFilter(@"/sbin/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Logs/");
                yield return DefaultFilters.CreateWildcardFilter(@"/Network/");
                yield return DefaultFilters.CreateWildcardFilter(@"/System/");
                yield return DefaultFilters.CreateWildcardFilter(@"/Volumes/");
            }

            if (group.HasFlag(DefaultFilterGroup.CacheFiles))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"*/Application Support/Google/Chrome/Default/Cookies");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Application Support/Google/Chrome/Default/Cookies-journal");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Caches/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Calendars/*/Info.plist");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Calendars/Calendar Cache");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Cookies/com.apple.appstore.plist");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Cookies/Cookies.binarycookies");
                yield return DefaultFilters.CreateWildcardFilter(@"*/backups.backupdb/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/iP* Software Updates/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/iPhoto Library/iPod Photo Cache*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/iTunes/Album Artwork/Cache/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Application Support/SyncServices/");

                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Mail/*/Info.plist");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Mail/AvailableFeeds/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Mail/Envelope Index");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Mirrors/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/PubSub/Database/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/PubSub/Downloads/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/PubSub/Feeds/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Safari/HistoryIndex.sk");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Safari/Icons.db");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Safari/WebpageIcons.db");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Library/Saved Application State/");

                yield return DefaultFilters.CreateWildcardFilter(@"/System/Library/Extensions/Caches/");
                yield return DefaultFilters.CreateWildcardFilter(@"*MobileBackups/");

                yield return DefaultFilters.CreateWildcardFilter(@"*.hotfiles.btree*");
                yield return DefaultFilters.CreateWildcardFilter(@"*.Spotlight-*/");

                yield return DefaultFilters.CreateWildcardFilter(@"/Desktop DB");
                yield return DefaultFilters.CreateWildcardFilter(@"/Desktop DF");
            }

            if (group.HasFlag(DefaultFilterGroup.TemporaryFolders))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"*/Microsoft User Data/Entourage Temp/");
                yield return DefaultFilters.CreateWildcardFilter(@"/tmp/");
                yield return DefaultFilters.CreateWildcardFilter(@"/var/");
                yield return DefaultFilters.CreateWildcardFilter(@"*.Trash*");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Network Trash Folder/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/Trash/");

                yield return DefaultFilters.CreateWildcardFilter(@"*/lost+found/");
                yield return DefaultFilters.CreateWildcardFilter(@"*/VM Storage");
            }

            if (group.HasFlag(DefaultFilterGroup.Applications))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"/Applications/");
                yield return DefaultFilters.CreateWildcardFilter(@"/Library/");
                yield return DefaultFilters.CreateWildcardFilter(@"/usr/");
                yield return DefaultFilters.CreateWildcardFilter(@"/opt/");
            }
        }

        /// <summary>
        /// Creates Linux filters
        /// </summary>
        /// <param name="group">The groups to create the filters for</param>
        /// <returns>Linux filters</returns>
        private static IEnumerable<string> CreateLinuxFilters(DefaultFilterGroup group)
        {
            if (group.HasFlag(DefaultFilterGroup.SystemFiles))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"/dev/");
                yield return DefaultFilters.CreateWildcardFilter(@"/proc/");
                yield return DefaultFilters.CreateWildcardFilter(@"/selinux/");
                yield return DefaultFilters.CreateWildcardFilter(@"/sys/");
            }
            if (group.HasFlag(DefaultFilterGroup.OperatingSystem))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"/bin/");
                yield return DefaultFilters.CreateWildcardFilter(@"/boot/");
                yield return DefaultFilters.CreateWildcardFilter(@"/etc/");
                yield return DefaultFilters.CreateWildcardFilter(@"/initrd/");
                yield return DefaultFilters.CreateWildcardFilter(@"/sbin/");
                yield return DefaultFilters.CreateWildcardFilter(@"/var/");
            }
                
            if (group.HasFlag(DefaultFilterGroup.TemporaryFolders))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"*/lost+found/");
                yield return DefaultFilters.CreateWildcardFilter(@"*~");
                yield return DefaultFilters.CreateWildcardFilter(@"/tmp/");
            }
            if (group.HasFlag(DefaultFilterGroup.CacheFiles))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"*/.config/google-chrome/Default/Cookies");
                yield return DefaultFilters.CreateWildcardFilter(@"*/.config/google-chrome/Default/Cookies-journal");
            }
            if (group.HasFlag(DefaultFilterGroup.Applications))
            {
                yield return DefaultFilters.CreateWildcardFilter(@"/lib/");
                yield return DefaultFilters.CreateWildcardFilter(@"/lib64/");
                yield return DefaultFilters.CreateWildcardFilter(@"/opt/");
                yield return DefaultFilters.CreateWildcardFilter(@"/usr/");
            }
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
        /// Gets a list of exclude paths from the MacOS system
        /// </summary>
        /// <returns>The list of paths to exclude on OSX backups.</returns>
        private static IEnumerable<string> GetOSXExcludeFiles()
        {
            var res = new List<string>();
            if (Utility.IsClientOSX)
            {
                try
                {
                    //TODO: Consider the dynamic list:
                    // ~> sudo mdfind "com_apple_backup_excludeItem = 'com.apple.backupd'"

                    var doc = System.Xml.Linq.XDocument.Load("/System/Library/CoreServices/backupd.bundle/Contents/Resources/StdExclusions.plist");
                    var toplevel = doc.Element("plist")?.Element("dict")?.Elements("key");

                    foreach (var n in toplevel)
                    {
                        if (new string[] { "PathsExcluded", "ContentsExcluded", "FileContentsExcluded" }.Contains(n.Value, StringComparer.Ordinal))
                        {
                            if (n.NextNode is System.Xml.Linq.XContainer)
                                foreach (var p in ((System.Xml.Linq.XContainer)n.NextNode).Elements("string"))
                                {
                                    if (System.IO.File.Exists(p.Value))
                                        res.Add(p.Value);
                                    else if (System.IO.Directory.Exists(p.Value))
                                        res.Add(p.Value + "/");
                                    else
                                        res.Add(p.Value);
                                }
                                
                        }
                        else if (string.Equals(n.Value, "UserPathsExcluded", StringComparison.Ordinal))
                        {
                            // TODO: We need to figure out how to map the paths to either a file or a folder.
                            // alternatively, we can use a regex with an optional trailing slash,
                            // but this has a large performance overhead, so for now the code below
                            // works but has been commented out

                            /*
                            if (n.NextNode is System.Xml.Linq.XContainer)
                                foreach (var p in ((System.Xml.Linq.XContainer)n.NextNode).Elements("string"))
                                    res.Add("[/Users/[^/]+/" + System.Text.RegularExpressions.Regex.Escape(p.Value) + "/?]");
                            */
                        }
                    }

                    return res;
                }
                catch
                {
                }
            }

            return null;
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
