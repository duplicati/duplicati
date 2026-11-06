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
using System.CommandLine;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.DatabaseTool.Commands;

/// <summary>
/// The downgrade command
/// </summary>
public static class Upgrade
{
    /// <summary>
    /// Creates the downgrade command
    /// </summary>
    /// <returns>The downgrade command</returns>
    public static Command Create()
    {
        var databasesArgument = new Argument<string[]>("databases")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "The databases to upgrade"
        };
        var serverDatafolderOption = new Option<DirectoryInfo>("--server-datafolder")
        {
            Description = "The folder with databases",
            DefaultValueFactory = _ => new DirectoryInfo(DataFolderLocator.GetDefaultStorageFolder(DataFolderManager.SERVER_DATABASE_FILENAME, false, true))
        };
        var serverVersionOption = new Option<int>("--server-version")
        {
            Description = "The version to upgrade the server database to; zero or less means latest",
            DefaultValueFactory = _ => 0
        };
        var localVersionOption = new Option<int>("--local-version")
        {
            Description = "The version to upgrade local databases to; zero or less means latest",
            DefaultValueFactory = _ => 0
        };
        var syncVersionOption = new Option<int>("--sync-version")
        {
            Description = "The version to upgrade sync databases to; zero or less means latest",
            DefaultValueFactory = _ => 0
        };
        var noBackupsOption = new Option<bool>("--no-backups")
        {
            Description = "Do not create backups before upgrade",
            DefaultValueFactory = _ => false
        };
        var includeUntrackedDatabasesOption = new Option<bool>("--include-untracked-databases")
        {
            Description = "Include untracked databases in the upgrade process",
            DefaultValueFactory = _ => false
        };

        var cmd = new Command("upgrade", "Upgrades one or more databases to a larger version")
        {
            databasesArgument,
            serverDatafolderOption,
            serverVersionOption,
            localVersionOption,
            syncVersionOption,
            noBackupsOption,
            includeUntrackedDatabasesOption
        };

        cmd.SetAction(parseResult =>
        {
            var databases = parseResult.GetValue(databasesArgument) ?? [];
            var serverdatafolder = parseResult.GetValue(serverDatafolderOption)!;
            var serverversion = parseResult.GetValue(serverVersionOption);
            var localversion = parseResult.GetValue(localVersionOption);
            var syncversion = parseResult.GetValue(syncVersionOption);
            var nobackups = parseResult.GetValue(noBackupsOption);
            var includeuntrackeddatabases = parseResult.GetValue(includeUntrackedDatabasesOption);

            databases = Helper.FindAllDatabasesAsync(databases, serverdatafolder.FullName, includeuntrackeddatabases).Await();
            if (databases.Length == 0)
            {
                Console.WriteLine("No databases found to upgrade");
                return;
            }

            var serverScripts = DatabaseUpgrader.GetSchemaScripts(typeof(Library.RestAPI.Database.DatabaseSchemaMarker));
            var localScripts = DatabaseUpgrader.GetSchemaScripts(typeof(Library.Main.Database.Local.DatabaseSchemaMarker));
            var syncScripts = DatabaseUpgrader.GetSchemaScripts(typeof(Library.Main.Database.Sync.DatabaseSchemaMarker));
            var serverVersions = serverScripts.Upgrades;
            var localVersions = localScripts.Upgrades;
            var syncVersions = syncScripts.Upgrades;

            if (serverversion <= 0)
                serverversion = serverVersions.Max(x => x.Version);
            if (localversion <= 0)
                localversion = localVersions.Max(x => x.Version);
            if (syncversion <= 0)
                syncversion = syncVersions.Any() ? syncVersions.Max(x => x.Version) : 1;

            if (serverversion > serverVersions.Max(x => x.Version))
                throw new UserInformationException($"Server version {serverversion} is greater than the latest version {serverVersions.Max(x => x.Version)}", "UnsupportedUpgradeVersion");
            if (localversion > localVersions.Max(x => x.Version))
                throw new UserInformationException($"Local version {localversion} is greater than the latest version {localVersions.Max(x => x.Version)}", "UnsupportedUpgradeVersion");
            var maxSyncVersion = syncVersions.Any() ? syncVersions.Max(x => x.Version) : 1;
            if (syncversion > maxSyncVersion)
                throw new UserInformationException($"Sync version {syncversion} is greater than the latest version {maxSyncVersion}", "UnsupportedUpgradeVersion");

            foreach (var db in databases)
            {
                Console.WriteLine($"Examining {db} ...");
                if (!File.Exists(db))
                {
                    Console.WriteLine($"Database {db} does not exist");
                    continue;
                }

                int version;
                DatabaseType type;
                try
                {
                    (version, type) = Helper.ExamineDatabaseAsync(db).Await();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading database {db}: {ex.Message}");
                    continue;
                }

                var targetVersion = type switch
                {
                    DatabaseType.Server => serverversion,
                    DatabaseType.Sync => syncversion,
                    DatabaseType.Backup => localversion,
                    _ => throw new Exception($"Unknown database type: {type}")
                };

                var scripts = type switch
                {
                    DatabaseType.Server => serverScripts,
                    DatabaseType.Sync => syncScripts,
                    DatabaseType.Backup => localScripts,
                    _ => throw new Exception($"Unknown database type: {type}")
                };

                Console.WriteLine($"Database {db} is version {version} and is a {type} database");
                ApplyUpgradeAsync(db, version, targetVersion, scripts, nobackups).Await();
            }
        });

        return cmd;
    }

    /// <summary>
    /// Applies the upgrade scripts to the database
    /// </summary>
    /// <param name="db">The database path</param>
    /// <param name="dbversion">The current database version</param>
    /// <param name="targetversion">The target database version</param>
    /// <param name="schemaScripts">The scripts bundled with this release</param>
    /// <param name="nobackups">Flag to disable backups</param>
    /// <returns>A task that completes when the upgrade is done.</returns>
    private static async Task ApplyUpgradeAsync(string db, int dbversion, int targetversion, SchemaScriptSet schemaScripts, bool nobackups)
    {
        var scripts = schemaScripts.Upgrades;
        if (targetversion > dbversion)
        {
            var requiredVersions = Enumerable.Range(dbversion + 1, targetversion - dbversion)
                .ToList();

            var missingScripts = requiredVersions
                .Where(x => !scripts.Any(y => y.Version == x))
                .ToList();

            if (missingScripts.Count > 0)
            {
                Console.WriteLine($"Missing upgrade scripts for versions: {string.Join(", ", missingScripts)}");
                return;
            }

            var upgradeScripts = requiredVersions
                .Select(x => scripts.First(y => y.Version == x))
                .ToList();

            Console.WriteLine($"Upgrading {db} from version {dbversion} to {targetversion}");
            if (!nobackups)
                Helper.CreateFileBackup(db);

            await using var con = await SQLiteLoader.LoadConnectionAsync(db)
                .ConfigureAwait(false);
            await using var tr = con.BeginTransaction();
            await using var cmd = con.CreateCommand(tr);
            foreach (var script in upgradeScripts)
            {
                Console.WriteLine($"Applying upgrade script {script.Filename} ...");
                try
                {
                    cmd.SetCommandAndParameters(script.Content);
                    var r = await cmd.ExecuteScalarAsync()
                        .ConfigureAwait(false);
                    if (r != null)
                        throw new UserInformationException($"{r}", "UpgradeScriptFailure");
                    await cmd.SetCommandAndParameters(@"
                        UPDATE ""Version""
                        SET ""Version"" = @Version
                    ")
                        .SetParameterValue("@Version", script.Version)
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new UserInformationException($"Failed applying upgrade script {script.Filename}: {ex.Message}", "DowngradeScriptFailure", ex);
                }
            }

            // Record the scripts so an older release can downgrade the database again
            DatabaseUpgrader.WriteSchemaScriptTable(con, tr, schemaScripts, targetversion);

            tr.Commit();
            Console.WriteLine($"Upgraded {db} to version {targetversion}");
        }
        else if (dbversion == targetversion)
        {
            Console.WriteLine($"Database {db} is already at version {targetversion}");
        }
        else
        {
            Console.WriteLine($"Database {db} is already at version {dbversion} (cannot upgrade to {targetversion})");
        }
    }
}
