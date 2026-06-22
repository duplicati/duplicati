// Copyright (C) 2026, The Duplicati Team
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
using System.Linq;
using System.Runtime.Versioning;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Snapshots.Windows;

namespace Duplicati.Library.Modules.Builtin
{
    /// <summary>
    /// Provides options for Microsoft SQL Server backup.
    /// </summary>
    public class MSSQLOptions : Interface.IGenericSourceModule
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<MSSQLOptions>();

        /// <summary>
        /// The prefix used for MSSQL paths.
        /// </summary>
        private const string MSSQLPrefix = @"%MSSQL%";

        /// <summary>
        /// A target database from a source path
        /// </summary>
        public sealed record TargetDb
        {
            /// <summary>
            /// The original path
            /// </summary>
            public required string Path { get; init; }
            /// <summary>
            /// The database name
            /// </summary>
            public required string Database { get; init; }
            /// <summary>
            /// The server name
            /// </summary>
            public required string Server { get; init; }
            /// <summary>
            /// The instance id
            /// </summary>
            public required string InstanceId { get; init; }
        }

        #region IGenericModule Members

        /// <summary>
        /// Gets the key identifier for this module.
        /// </summary>
        public string Key => "mssql-options";

        /// <summary>
        /// Gets the display name for this module.
        /// </summary>
        public string DisplayName => Strings.MSSQLOptions.DisplayName;

        /// <summary>
        /// Gets the description of this module.
        /// </summary>
        public string Description => Strings.MSSQLOptions.Description;

        /// <summary>
        /// Gets whether this module should be loaded by default.
        /// </summary>
        public bool LoadAsDefault => OperatingSystem.IsWindows();

        /// <summary>
        /// Gets the list of supported command line arguments.
        /// </summary>
        public IList<Interface.ICommandLineArgument> SupportedCommands => null;

        /// <summary>
        /// Configures the module with the provided command line options.
        /// </summary>
        /// <param name="commandlineOptions">The command line options dictionary.</param>
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            // Do nothing. Implementation needed for IGenericModule interface.
        }

        #endregion

        #region Implementation of IGenericSourceModule

        /// <summary>
        /// Parses the source paths for Microsoft SQL Server backups.
        /// </summary>
        /// <param name="paths">The source paths.</param>
        /// <param name="filter">The filter string.</param>
        /// <param name="commandlineOptions">The command line options.</param>
        /// <returns>A dictionary of changed options.</returns>
        public Dictionary<string, string> ParseSourcePaths(ref string[] paths, ref string filter, Dictionary<string, string> commandlineOptions)
        {
            // Early exit in case we are non-windows to prevent attempting to load Windows-only components
            if (!OperatingSystem.IsWindows())
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MSSqlWindowsOnly", null, "Microsoft SQL Server databases backup works only on Windows OS");

                // The code here is a bit defensive, but it basically removes any traces of MSSQL paths
                if (paths != null)
                    paths = paths.Where(x => !x.StartsWith(MSSQLPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (!string.IsNullOrEmpty(filter))
                {
                    var filters = filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);
                    var remainingfilters = filters.Where(x => x.Length > 1 && !x.Substring(1).StartsWith(MSSQLPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                    filter = string.Join(System.IO.Path.PathSeparator.ToString(), remainingfilters);
                }

                return new Dictionary<string, string>();
            }

            // Windows, do the real stuff!
            return RealParseSourcePaths(ref paths, ref filter, commandlineOptions);
        }

        private static TargetDb ParsePathEntry(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(MSSQLPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var parts = path.Split(['\\'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || !parts[0].Equals(MSSQLPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            return parts.Length switch
            {
                // Match all MSSQL databases
                1 => new TargetDb() { Path = path, Server = "", InstanceId = "", Database = "" },
                // Match a server
                2 => new TargetDb() { Path = path, Server = parts[1], InstanceId = "", Database = "" },
                // Match a server instance, or database on default server instance
                3 => new TargetDb() { Path = path, Server = parts[1], InstanceId = parts[2], Database = "" },
                // Match a database on a server instance
                4 => new TargetDb() { Path = path, Server = parts[1], InstanceId = parts[2], Database = parts[3] },
                _ => null
            };
        }

        /// <summary>
        /// Parses the source paths for Microsoft SQL Server backups (real implementation).
        /// </summary>
        /// <param name="paths">The source paths.</param>
        /// <param name="filter">The filter string.</param>
        /// <param name="commandlineOptions">The command line options.</param>
        /// <returns>A dictionary of changed options.</returns>
        // Make sure the JIT does not attempt to inline this call and thus load
        // referenced types from System.Management here
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [SupportedOSPlatform("windows")]
        public Dictionary<string, string> RealParseSourcePaths(ref string[] paths, ref string filter, Dictionary<string, string> commandlineOptions, IMSSQLUtility mssqlUtility = null)
        {
            var changedOptions = new Dictionary<string, string>();
            var mssqlfilters = new List<string>();

            if (!string.IsNullOrEmpty(filter))
            {
                var remainingFilters = new List<string>();
                var filters = filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var f in filters)
                {
                    if (f.Contains(MSSQLPrefix, StringComparison.OrdinalIgnoreCase))
                        mssqlfilters.Add(f);
                    else
                        remainingFilters.Add(f);
                }

                filter = string.Join(System.IO.Path.PathSeparator.ToString(), remainingFilters);
            }

            mssqlUtility ??= new MSSQLUtility();

            if (paths == null || !ContainFilesForBackup(paths) || !mssqlUtility.IsMSSQLInstalled)
                return changedOptions;

            if (commandlineOptions.Keys.Contains("vss-exclude-writers"))
            {
                var excludedWriters = commandlineOptions["vss-exclude-writers"].Split(';').Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length > 0).Select(x => new Guid(x)).ToArray();

                if (excludedWriters.Contains(mssqlUtility.MSSQLWriterGuid))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "CannotExcludeMsSqlVSSWriter", null, "Excluded writers for VSS cannot contain MS SQL writer when backuping Microsoft SQL Server databases. Removing \"{0}\" to continue", mssqlUtility.MSSQLWriterGuid.ToString());
                    changedOptions["vss-exclude-writers"] = string.Join(";", excludedWriters.Where(x => x != mssqlUtility.MSSQLWriterGuid));
                }
            }

            if (!commandlineOptions.Keys.Contains("snapshot-policy") || !commandlineOptions["snapshot-policy"].Equals("required", StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "MustSetSnapshotPolicy", null, "Snapshot policy have to be set to \"required\" when backuping Microsoft SQL Server databases. Changing to \"required\" to continue");
                changedOptions["snapshot-policy"] = "required";
            }

            Logging.Log.WriteInformationMessage(LOGTAG, "StartingMsSqlQuery", "Starting to gather Microsoft SQL Server information", Logging.LogMessageType.Information);
            var provider = Utility.Utility.ParseEnumOption(changedOptions.AsReadOnly(), "snapshot-provider", WindowsSnapshot.DEFAULT_WINDOWS_SNAPSHOT_QUERY_PROVIDER);
            mssqlUtility.QueryDBsInfo(provider);
            Logging.Log.WriteInformationMessage(LOGTAG, "MsSqlDatabaseCount", "Found {0} databases on Microsoft SQL Server", mssqlUtility.DBs.Count);

            foreach (var db in mssqlUtility.DBs)
                Logging.Log.WriteProfilingMessage(LOGTAG, "MsSqlDatabaseName", "Found DB name {0}, Server {1}, Instance {2}, files {3}", db.Database, db.Server, db.InstanceId, string.Join(";", db.DataPaths));

            var includedDbs = new List<TargetDb>();
            var nonMatchedPaths = new List<string>();

            foreach (var path in paths)
            {
                var match = ParsePathEntry(path);
                if (match == null)
                    nonMatchedPaths.Add(path);
                else
                    includedDbs.Add(match);
            }

            var dbsForBackup = new List<MSSQLDB>();
            var serverInstanceMap = mssqlUtility.DBs
                .GroupBy(x => x.ServerInstanceId)
                .ToDictionary(x => x.Key, x => x.GroupBy(y => y.Database).ToDictionary(y => y.Key, y => y.ToList(), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            var serverMap = mssqlUtility.DBs
                .GroupBy(x => x.Server)
                .ToDictionary(x => x.Key, x => x.GroupBy(y => y.Database).ToDictionary(y => y.Key, y => y.ToList(), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            foreach (var db in includedDbs)
            {
                // Catch-all case
                if (string.IsNullOrWhiteSpace(db.Server))
                {
                    dbsForBackup.AddRange(mssqlUtility.DBs);
                    continue;
                }

                if (!serverMap.TryGetValue(db.Server, out var serverDbs))
                    throw new Duplicati.Library.Interface.UserInformationException($"Server name specified in path as \"{db.Path}\" cannot be found", "MsSqlServerNotFound");


                // No instance id, so grab everything from that server
                if (string.IsNullOrWhiteSpace(db.InstanceId))
                {
                    dbsForBackup.AddRange(serverDbs.SelectMany(x => x.Value));
                    continue;
                }

                // Fully qualified name server\instance\database
                if (!string.IsNullOrWhiteSpace(db.Database))
                {
                    if (!serverInstanceMap.TryGetValue($"{db.Server}\\{db.InstanceId}", out var mappedServerInstance))
                        throw new Library.Interface.UserInformationException($"Server instance id specified in path as \"{db.Path}\" cannot be found", "MsSqlServerInstanceNotFound");

                    if (!mappedServerInstance.TryGetValue(db.Database, out var mappedList))
                        throw new Library.Interface.UserInformationException($"Database name specified in path as \"{db.Path}\" cannot be found", "MsSqlDatabaseNotFound");

                    dbsForBackup.AddRange(mappedList);
                    continue;
                }

                // At this point we have a server name and one more identifier
                // It could be a database name or an instance id
                // Either server\instance or server\database, but we don't know which one it is

                // If we match on the instance id, grab that
                var matchesInstance = serverInstanceMap.TryGetValue($"{db.Server}\\{db.InstanceId}", out var serverInstanceDbs);
                var matchesDb = serverDbs.TryGetValue(db.InstanceId, out var dbList);

                if (matchesInstance && matchesDb)
                    throw new Library.Interface.UserInformationException($"Server instance id specified in path as \"{db.Path}\" is ambiguous", "MsSqlServerInstanceAmbiguous");

                if (matchesInstance)
                    dbsForBackup.AddRange(serverInstanceDbs.SelectMany(x => x.Value));
                else if (matchesDb)
                    dbsForBackup.AddRange(dbList);
                else
                    throw new Library.Interface.UserInformationException($"Server instance id specified in path as \"{db.Path}\" cannot be found", "MsSqlServerInstanceNotFound");

            }

            // Merge duplicates that we may have picked up prior to applying the filter
            dbsForBackup = dbsForBackup
                .GroupBy(x => (x.Server, x.InstanceId, x.Database), x => x.DataPaths)
                .Select(x => new MSSQLDB
                {
                    Server = x.Key.Server,
                    InstanceId = x.Key.InstanceId,
                    Database = x.Key.Database,
                    DataPaths = x.SelectMany(y => y).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                })
                .ToList();


            if (mssqlfilters.Count > 0)
            {
                var resolvedFilter = Utility.FilterExpression.Deserialize(mssqlfilters.ToArray());
                Utility.FilterExpression.AnalyzeFilters(resolvedFilter, out var filtersInclude, out var filtersExclude);

                var defaultExclude = filtersInclude && !filtersExclude; // true = exclude unmatched, false = include unmatched

                dbsForBackup = dbsForBackup
                    .Where(x =>
                    {
                        // We need to emulate walking the tree
                        var virtualPath = $"{MSSQLPrefix}\\{x.ServerInstanceId}\\{x.Database}";
                        var segments = virtualPath.Split(['\\'], StringSplitOptions.RemoveEmptyEntries);
                        var current = string.Empty;

                        for (var i = 0; i < segments.Length - 1; i++)
                        {
                            current += Util.AppendDirSeparator(segments[i]);
                            // If a parent path is filtered, exit
                            if (resolvedFilter.Matches(current, out var treeResult, out var _) && !treeResult)
                                return false;
                        }

                        // Check the actual path
                        if (!resolvedFilter.Matches(virtualPath, out var result, out var _))
                            return !defaultExclude;

                        return result;
                    }).ToList();
            }
            var dbPaths = dbsForBackup.SelectMany(x => x.DataPaths).ToList();
            paths = nonMatchedPaths
                .Concat(dbPaths)
                .Distinct(Utility.Utility.ClientFilenameStringComparer)
                .ToArray();

            return changedOptions;
        }

        /// <summary>
        /// Determines whether the paths contain files for Microsoft SQL Server backup.
        /// </summary>
        /// <param name="paths">The paths to check.</param>
        /// <returns>True if the paths contain Microsoft SQL Server files for backup.</returns>
        public bool ContainFilesForBackup(string[] paths)
        {
            if (paths == null || !OperatingSystem.IsWindows())
                return false;

            return paths.Where(x => !string.IsNullOrWhiteSpace(x))
                .Any(x => x.StartsWith(MSSQLPrefix, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
