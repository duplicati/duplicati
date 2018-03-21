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
    public enum FilterGroup
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
        /// Meta option that specifies the default exclude filters
        /// </summary>
        DefaultExclude = All,

        /// <summary>
        /// Meta option that specifies the default include filters
        /// </summary>
        DefaultInclude = None,
    }

    /// <summary>
    /// This class defines a set of common filters for files that don't typically need to be backed up.
    /// These filters are largely based on the filters described here:
    /// https://superuser.com/questions/443890/what-files-file-types-and-folders-to-exclude-from-user-folder-backup
    /// which are in turn based on the filters defined by Crashplan:
    /// https://support.code42.com/CrashPlan/4/Troubleshooting/What_is_not_backing_up#Admin_Excludes
    /// </summary>
    public static class FilterGroups
    {
        /// <summary>
        /// In addition to the default names from the enums, these alternate / shorter names are also available.
        /// </summary>
        private static Dictionary<string, FilterGroup> filterGroupAliases = new Dictionary<string, FilterGroup>(StringComparer.OrdinalIgnoreCase)
            {
                { "System", FilterGroup.SystemFiles },

                { "OS", FilterGroup.OperatingSystem },

                { "CacheFolders", FilterGroup.CacheFiles },
                { "Caches", FilterGroup.CacheFiles },
                { "Cache", FilterGroup.CacheFiles },

                { "TemporaryFiles", FilterGroup.TemporaryFolders },
                { "TempFolders", FilterGroup.TemporaryFolders },
                { "TempFiles", FilterGroup.TemporaryFolders },
                { "Temp", FilterGroup.TemporaryFolders },

                { "Apps", FilterGroup.Applications },
            };

        /// <summary>
        /// Gets the list of alternate aliases which can refer to this group.
        /// </summary>
        /// <param name="group">Filter group</param>
        /// <returns>Group aliases</returns>
        public static IEnumerable<string> GetAliases(FilterGroup group)
        {
            return FilterGroups.filterGroupAliases.Where(pair => pair.Value == group).Select(pair => pair.Key);
        }

        /// <summary>
        /// Helper method that parses a string with a number of options and returns the group that it represents
        /// </summary>
        /// <returns>The filters specified by the group.</returns>
        /// <param name="filters">The filter string.</param>
        /// <param name="defaultValue">The value to use if there are no filter groups specified in the string</param>
        public static FilterGroup ParseFilterList(string filters, FilterGroup defaultValue = FilterGroup.None)
        {
            var res = FilterGroup.None;
            var any = false;
            foreach (var f in (filters ?? string.Empty).Split(new char[] { ':', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                any = true;
                if (!Enum.TryParse<FilterGroup>(f, true, out var fg) &&
                    !FilterGroups.filterGroupAliases.TryGetValue(f, out fg))
                {
                    throw new ArgumentException(Strings.Filters.UnknownFilterGroup(f));
                }

                res |= fg;
            }

            if (!any)
                res = defaultValue;

            return res;
        }

        /// <summary>
        /// Gets a string that describes the filter group options
        /// </summary>
        /// <returns>The option descriptions.</returns>
        /// <param name="indentation">The string indentation to use.</param>
        /// <param name="includevalues">If set to <c>true</c> include actual filter paths in the output.</param>
        public static string GetOptionDescriptions(int indentation, bool includevalues)
        {
            var defaultExclude = FilterGroup.DefaultExclude;
            var defaultExcludeValues = new List<string>();
            var defaultInclude = FilterGroup.DefaultInclude;
            var defaultIncludeValues = new List<string>();
            foreach (var e in Enum.GetValues(typeof(FilterGroup)))
            {
                var ed = (FilterGroup)e;
                if (defaultExclude.HasFlag(ed) && ed != defaultExclude && ed != FilterGroup.All && ed != FilterGroup.None)
                    defaultExcludeValues.Add(e.ToString());
                if (defaultInclude.HasFlag(ed) && ed != defaultInclude && ed != FilterGroup.All && ed != FilterGroup.None)
                    defaultIncludeValues.Add(e.ToString());
            }

            var ind = new string(' ', indentation);
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(ind + LC.L("{0}: Selects no filters.", nameof(FilterGroup.None)));
            sb.AppendLine(ind + LC.L("{0}: Selects all the filter groups.", nameof(FilterGroup.All)));
            sb.AppendLine(ind + LC.L("{0}: A set of default exclude filters, currently evaluates to: {1}.", nameof(FilterGroup.DefaultExclude), FilterGroup.DefaultExclude == FilterGroup.All ? nameof(FilterGroup.All) : string.Join(",", defaultExcludeValues.DefaultIfEmpty(nameof(FilterGroup.None)))));
            sb.AppendLine(ind + LC.L("{0}: A set of default include filters, currently evaluates to: {1}.", nameof(FilterGroup.DefaultInclude), FilterGroup.DefaultInclude == FilterGroup.All ? nameof(FilterGroup.All) : string.Join(",", defaultIncludeValues.DefaultIfEmpty(nameof(FilterGroup.None)))));

            if (includevalues)
                sb.AppendLine();

            Action<FilterGroup, bool> appendAliasesAndValues = (filterGroup, lastLine) =>
            {
                if (includevalues)
                {
                    if (FilterGroups.GetAliases(filterGroup).Any())
                    {
                        sb.AppendLine(ind + LC.L(" Aliases: {0}", string.Join(",", FilterGroups.GetAliases(filterGroup).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))));
                    }
                    foreach (var v in GetFilterStrings(filterGroup))
                    {
                        sb.AppendLine(ind + "  " + v);
                    }
                    if (!lastLine)
                    {
                        sb.AppendLine();
                    }
                }
            };
            
            sb.AppendLine(ind + LC.L("{0}: Files that are owned by the system or not suited to be backed up. This includes any operating system reported protected files. Most users should at least apply these filters.", nameof(FilterGroup.SystemFiles)));
            appendAliasesAndValues(FilterGroup.SystemFiles, false);
            sb.AppendLine(ind + LC.L("{0}: Files that belong to the operating system. These files are restored when the operating system is re-installed.", nameof(FilterGroup.OperatingSystem)));
            appendAliasesAndValues(FilterGroup.OperatingSystem, false);
            sb.AppendLine(ind + LC.L("{0}: Files and folders that are known to be storage of temporary data.", nameof(FilterGroup.TemporaryFolders)));
            appendAliasesAndValues(FilterGroup.TemporaryFolders, false);
            sb.AppendLine(ind + LC.L("{0}: Files and folders that are known cache locations for the operating system and various applications", nameof(FilterGroup.CacheFiles)));
            appendAliasesAndValues(FilterGroup.CacheFiles, false);
            sb.AppendLine(ind + LC.L("{0}: Installed programs and their libraries, but not their settings.", nameof(FilterGroup.Applications)));
            appendAliasesAndValues(FilterGroup.Applications, true);

            return sb.ToString();
        }

        /// <summary>
        /// Gets the filters for a specific group
        /// </summary>
        /// <returns>The filters from the group.</returns>
        /// <param name="group">The group to use.</param>
        public static IEnumerable<string> GetFilterStrings(FilterGroup group)
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
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                    )
                    .Distinct(Utility.ClientFilenameStringComparer);
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
        private static IEnumerable<string> CreateCommonFilters(FilterGroup group)
        {
            if (group.HasFlag(FilterGroup.CacheFiles))
            {
                yield return FilterGroups.CreateWildcardFilter(@"*/Google/Chrome/*cache*");
                yield return FilterGroups.CreateWildcardFilter(@"*/Google/Chrome/Safe Browsing*");
                yield return FilterGroups.CreateWildcardFilter(@"*/iPhoto Library/iPod Photo Cache/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Mozilla/Firefox/*cache*");
                yield return FilterGroups.CreateRegexFilter(@".*/(cookies|permissions).sqllite(-.{3})?");
            }

            if (group.HasFlag(FilterGroup.TemporaryFolders))
            {
                yield return Library.Utility.TempFolder.SystemTempPath;
            }
        }

        /// <summary>
        /// Creates Windows filters
        /// </summary>
        /// <param name="group">The groups to create the filters for</param>
        /// <returns>Windows filters</returns>
        private static IEnumerable<string> CreateWindowsFilters(FilterGroup group)
        {
            if (group.HasFlag(FilterGroup.SystemFiles))
            {
                yield return FilterGroups.CreateWildcardFilter(@"*/I386*");
                yield return FilterGroups.CreateWildcardFilter(@"*/Internet Explorer/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Microsoft*/RecoveryStore*");
                yield return FilterGroups.CreateWildcardFilter(@"*/Microsoft*/Windows/*.edb");
                yield return FilterGroups.CreateWildcardFilter(@"*/Microsoft*/Windows/*.log");
                yield return FilterGroups.CreateWildcardFilter(@"*/Microsoft*/Windows/Cookies*");
                yield return FilterGroups.CreateWildcardFilter(@"*/MSOCache*");
                yield return FilterGroups.CreateWildcardFilter(@"*/NTUSER*");
                yield return FilterGroups.CreateWildcardFilter(@"*/RECYCLER/");
                yield return FilterGroups.CreateWildcardFilter(@"*UsrClass.dat");
                yield return FilterGroups.CreateWildcardFilter(@"*UsrClass.dat.LOG");
                yield return FilterGroups.CreateWildcardFilter(@"?:/hiberfil.sys");
                yield return FilterGroups.CreateWildcardFilter(@"?:/pagefile.sys");
                yield return FilterGroups.CreateWildcardFilter(@"?:/swapfile.sys");
                yield return FilterGroups.CreateWildcardFilter(@"?:/System Volume Information/");
                yield return FilterGroups.CreateWildcardFilter(@"?:/Windows/Installer*");
                yield return FilterGroups.CreateWildcardFilter(@"?:/Windows/Temp*");
                yield return FilterGroups.CreateWildcardFilter(@"*/ntuser.dat*");

                foreach (var s in GetWindowsRegistryFilters() ?? new string[0])
                    yield return s;
            }

            if (group.HasFlag(FilterGroup.OperatingSystem))
            {
                yield return FilterGroups.CreateWildcardFilter(@"?:/Config.Msi*"); // https://github.com/duplicati/duplicati/issues/2886
                yield return FilterGroups.CreateWildcardFilter(@"*/Recent/");
                yield return FilterGroups.CreateWildcardFilter(@"?:/autoexec.bat");
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

            if (group.HasFlag(FilterGroup.CacheFiles))
            {
                yield return FilterGroups.CreateWildcardFilter(@"*/AppData/Apple Computer/Mobile Sync/");
                yield return FilterGroups.CreateWildcardFilter(@"*/AppData/Local/Microsoft/Windows Store/");
                yield return FilterGroups.CreateWildcardFilter(@"*/AppData/Local/Packages/"); // https://superuser.com/questions/490925/explain-windows-8-windows-store-appdata-packages-and-what-to-backup
                yield return FilterGroups.CreateWildcardFilter(@"*/Application Data/Apple Computer/Mobile Sync/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Application Data/Application Data*");
                yield return FilterGroups.CreateWildcardFilter(@"*/Google/Chrome/User Data/Default/Cookies");
                yield return FilterGroups.CreateWildcardFilter(@"*/Google/Chrome/User Data/Default/Cookies-journal");
                yield return FilterGroups.CreateWildcardFilter(@"*/Thumbs.db");
                yield return FilterGroups.CreateWildcardFilter(@"*/Safari/Library/Caches/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Temporary Internet Files/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Local Settings/History/");

                yield return Environment.GetFolderPath(Environment.SpecialFolder.History);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.Cookies);

            }

            if (group.HasFlag(FilterGroup.TemporaryFolders))
            {
                yield return FilterGroups.CreateWildcardFilter(@"*/$RECYCLE.BIN/");
                yield return FilterGroups.CreateWildcardFilter(@"*.tmp");
                yield return FilterGroups.CreateWildcardFilter(@"*.tmp/");
                yield return FilterGroups.CreateWildcardFilter(@"*/AppData/Local/Temp*");
                yield return FilterGroups.CreateWildcardFilter(@"*/AppData/Temp*");
                yield return FilterGroups.CreateWildcardFilter(@"*/Local Settings/Temp*");
                yield return FilterGroups.CreateWildcardFilter(@"?:/Windows/Temp*");
            }

            if (group.HasFlag(FilterGroup.Applications))
            {
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
        private static IEnumerable<string> CreateOSXFilters(FilterGroup group)
        {
            if (group.HasFlag(FilterGroup.SystemFiles))
            {
                yield return FilterGroups.CreateWildcardFilter(@"/.vol/");
                yield return FilterGroups.CreateWildcardFilter(@"/dev/");
                yield return FilterGroups.CreateWildcardFilter(@"/net/");
                yield return FilterGroups.CreateWildcardFilter(@"/afs/");
                yield return FilterGroups.CreateWildcardFilter(@"/automount/");
                yield return FilterGroups.CreateWildcardFilter(@"/cores/");
                yield return FilterGroups.CreateWildcardFilter(@"/Network/");
                yield return FilterGroups.CreateWildcardFilter(@"*.fseventsd");
                yield return FilterGroups.CreateWildcardFilter(@"*.dbfseventsd");

                yield return FilterGroups.CreateWildcardFilter(@"/private/Network/");
                yield return FilterGroups.CreateWildcardFilter(@"/private/var/automount/");
                yield return FilterGroups.CreateWildcardFilter(@"/private/var/db/dhcpclient/");
                yield return FilterGroups.CreateWildcardFilter(@"/private/var/db/fseventsd/");
                yield return FilterGroups.CreateWildcardFilter(@"/private/var/folders/");
                yield return FilterGroups.CreateWildcardFilter(@"/private/var/run/");
                yield return FilterGroups.CreateWildcardFilter(@"/private/var/spool/postfix/");
                yield return FilterGroups.CreateWildcardFilter(@"/private/var/vm/");

                foreach (var p in GetOSXExcludeFiles() ?? new string[0])
                    yield return p;
            }

            if (group.HasFlag(FilterGroup.OperatingSystem))
            {
                yield return FilterGroups.CreateWildcardFilter(@"/Previous Systems*");
                yield return FilterGroups.CreateWildcardFilter(@"/mach.sym");
                yield return FilterGroups.CreateWildcardFilter(@"/mach_kernel");
                yield return FilterGroups.CreateWildcardFilter(@"/bin/");
                yield return FilterGroups.CreateWildcardFilter(@"/sbin/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Logs/");
                yield return FilterGroups.CreateWildcardFilter(@"/Network/");
                yield return FilterGroups.CreateWildcardFilter(@"/System/");
                yield return FilterGroups.CreateWildcardFilter(@"/Volumes/");
            }

            if (group.HasFlag(FilterGroup.CacheFiles))
            {
                yield return FilterGroups.CreateWildcardFilter(@"*/Application Support/Google/Chrome/Default/Cookies");
                yield return FilterGroups.CreateWildcardFilter(@"*/Application Support/Google/Chrome/Default/Cookies-journal");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Caches/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Calendars/*/Info.plist");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Calendars/Calendar Cache");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Cookies/com.apple.appstore.plist");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Cookies/Cookies.binarycookies");
                yield return FilterGroups.CreateWildcardFilter(@"*/backups.backupdb/");
                yield return FilterGroups.CreateWildcardFilter(@"*/iP* Software Updates/");
                yield return FilterGroups.CreateWildcardFilter(@"*/iPhoto Library/iPod Photo Cache*");
                yield return FilterGroups.CreateWildcardFilter(@"*/iTunes/Album Artwork/Cache/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Application Support/SyncServices/");

                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Mail/*/Info.plist");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Mail/AvailableFeeds/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Mail/Envelope Index");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Mirrors/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/PubSub/Database/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/PubSub/Downloads/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/PubSub/Feeds/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Safari/HistoryIndex.sk");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Safari/Icons.db");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Safari/WebpageIcons.db");
                yield return FilterGroups.CreateWildcardFilter(@"*/Library/Saved Application State/");

                yield return FilterGroups.CreateWildcardFilter(@"/System/Library/Extensions/Caches/");
                yield return FilterGroups.CreateWildcardFilter(@"*MobileBackups/");

                yield return FilterGroups.CreateWildcardFilter(@"*.hotfiles.btree*");
                yield return FilterGroups.CreateWildcardFilter(@"*.Spotlight-*/");

                yield return FilterGroups.CreateWildcardFilter(@"/Desktop DB");
                yield return FilterGroups.CreateWildcardFilter(@"/Desktop DF");
            }

            if (group.HasFlag(FilterGroup.TemporaryFolders))
            {
                yield return FilterGroups.CreateWildcardFilter(@"/private/tmp/");
                yield return FilterGroups.CreateWildcardFilter(@"/private/var/tmp/");

                yield return FilterGroups.CreateWildcardFilter(@"*/Microsoft User Data/Entourage Temp/");
                yield return FilterGroups.CreateWildcardFilter(@"/tmp/");
                yield return FilterGroups.CreateWildcardFilter(@"/var/");
                yield return FilterGroups.CreateWildcardFilter(@"*.Trash*");
                yield return FilterGroups.CreateWildcardFilter(@"*/Network Trash Folder/");
                yield return FilterGroups.CreateWildcardFilter(@"*/Trash/");

                yield return FilterGroups.CreateWildcardFilter(@"*/lost+found/");
                yield return FilterGroups.CreateWildcardFilter(@"*/VM Storage");
            }

            if (group.HasFlag(FilterGroup.Applications))
            {
                yield return FilterGroups.CreateWildcardFilter(@"/Applications/");
                yield return FilterGroups.CreateWildcardFilter(@"/Library/");
                yield return FilterGroups.CreateWildcardFilter(@"/usr/");
                yield return FilterGroups.CreateWildcardFilter(@"/opt/");
            }
        }

        /// <summary>
        /// Creates Linux filters
        /// </summary>
        /// <param name="group">The groups to create the filters for</param>
        /// <returns>Linux filters</returns>
        private static IEnumerable<string> CreateLinuxFilters(FilterGroup group)
        {
            if (group.HasFlag(FilterGroup.SystemFiles))
            {
                yield return FilterGroups.CreateWildcardFilter(@"/dev/");
                yield return FilterGroups.CreateWildcardFilter(@"/proc/");
                yield return FilterGroups.CreateWildcardFilter(@"/selinux/");
                yield return FilterGroups.CreateWildcardFilter(@"/sys/");
            }
            if (group.HasFlag(FilterGroup.OperatingSystem))
            {
                yield return FilterGroups.CreateWildcardFilter(@"/bin/");
                yield return FilterGroups.CreateWildcardFilter(@"/boot/");
                yield return FilterGroups.CreateWildcardFilter(@"/etc/");
                yield return FilterGroups.CreateWildcardFilter(@"/initrd/");
                yield return FilterGroups.CreateWildcardFilter(@"/sbin/");
                yield return FilterGroups.CreateWildcardFilter(@"/var/");
            }
                
            if (group.HasFlag(FilterGroup.TemporaryFolders))
            {
                yield return FilterGroups.CreateWildcardFilter(@"*/lost+found/");
                yield return FilterGroups.CreateWildcardFilter(@"*~");
                yield return FilterGroups.CreateWildcardFilter(@"/tmp/");
            }
            if (group.HasFlag(FilterGroup.CacheFiles))
            {
                yield return FilterGroups.CreateWildcardFilter(@"*/.config/google-chrome/Default/Cookies");
                yield return FilterGroups.CreateWildcardFilter(@"*/.config/google-chrome/Default/Cookies-journal");
            }
            if (group.HasFlag(FilterGroup.Applications))
            {
                yield return FilterGroups.CreateWildcardFilter(@"/lib/");
                yield return FilterGroups.CreateWildcardFilter(@"/lib64/");
                yield return FilterGroups.CreateWildcardFilter(@"/opt/");
                yield return FilterGroups.CreateWildcardFilter(@"/usr/");
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
