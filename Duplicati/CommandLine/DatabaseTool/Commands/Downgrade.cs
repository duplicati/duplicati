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
using System.Text.RegularExpressions;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.DatabaseTool.Commands;

/// <summary>
/// The downgrade command
/// </summary>
public static class Downgrade
{
    /// <summary>
    /// Matches SQL that reaches outside the database, which a script stored in the database may not use
    /// </summary>
    private static readonly Regex ForbiddenStoredSql = new(@"\b(ATTACH|DETACH|VACUUM|load_extension)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Creates the downgrade command
    /// </summary>
    /// <returns>The downgrade command</returns>
    public static Command Create()
    {
        var databasesArgument = new Argument<string[]>("databases")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "The databases to downgrade"
        };
        var serverDatafolderOption = new Option<DirectoryInfo>("--server-datafolder")
        {
            Description = "The folder with databases",
            DefaultValueFactory = _ => new DirectoryInfo(DataFolderLocator.GetDefaultStorageFolder(DataFolderManager.SERVER_DATABASE_FILENAME, false, true))
        };
        var serverVersionOption = new Option<int>("--server-version")
        {
            Description = "The version to downgrade the server database to; negative means the newest version this release supports",
            DefaultValueFactory = _ => -1
        };
        var localVersionOption = new Option<int>("--local-version")
        {
            Description = "The version to downgrade local databases to; negative means the newest version this release supports",
            DefaultValueFactory = _ => -1
        };
        var syncVersionOption = new Option<int>("--sync-version")
        {
            Description = "The version to downgrade sync databases to; negative means the newest version this release supports",
            DefaultValueFactory = _ => -1
        };
        var noBackupsOption = new Option<bool>("--no-backups")
        {
            Description = "Do not create backups before downgrade",
            DefaultValueFactory = _ => false
        };
        var includeUntrackedDatabasesOption = new Option<bool>("--include-untracked-databases")
        {
            Description = "Include untracked databases in the downgrade process",
            DefaultValueFactory = _ => false
        };

        var cmd = new Command("downgrade", "Downgrades one or more databases to a prior version")
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
                Console.WriteLine("No databases found to downgrade");
                return;
            }

            var serverScripts = DatabaseUpgrader.GetSchemaScripts(typeof(Library.RestAPI.Database.DatabaseSchemaMarker));
            var localScripts = DatabaseUpgrader.GetSchemaScripts(typeof(Library.Main.Database.Local.DatabaseSchemaMarker));
            var syncScripts = DatabaseUpgrader.GetSchemaScripts(typeof(Library.Main.Database.Sync.DatabaseSchemaMarker));

            if (serverversion < 0)
                serverversion = serverScripts.LatestVersion;
            if (localversion < 0)
                localversion = localScripts.LatestVersion;
            if (syncversion < 0)
                syncversion = syncScripts.LatestVersion;

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
                ApplyDowngradeAsync(db, version, targetVersion, scripts, nobackups).Await();
            }
        });

        return cmd;
    }

    /// <summary>
    /// A downgrade script recorded in the database's <c>SchemaScript</c> table
    /// </summary>
    /// <param name="Content">The script contents, or <c>null</c> if the version cannot be downgraded</param>
    /// <param name="WriterVersion">The newest schema version known by the release that wrote the row</param>
    private sealed record StoredScript(string? Content, int WriterVersion);

    /// <summary>
    /// A downgrade script chosen for one version
    /// </summary>
    /// <param name="Version">The version the script downgrades from</param>
    /// <param name="Description">The name shown while the script runs</param>
    /// <param name="Content">The script contents</param>
    /// <param name="FromDatabase">Whether the script was read from the database rather than bundled</param>
    private sealed record DowngradeStep(int Version, string Description, string Content, bool FromDatabase);

    /// <summary>
    /// Reads the downgrade scripts recorded in the database
    /// </summary>
    /// <param name="db">The database path</param>
    /// <returns>The scripts by version, or <c>null</c> if the database has no <c>SchemaScript</c> table</returns>
    private static async Task<Dictionary<int, StoredScript>?> ReadStoredScriptsAsync(string db)
    {
        await using var con = await SQLiteLoader.LoadConnectionAsync(db)
            .ConfigureAwait(false);
        await using var cmd = con.CreateCommand();

        var exists = await cmd.ExecuteScalarInt64Async(@"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'SchemaScript'
            ", CancellationToken.None)
            .ConfigureAwait(false);
        if (exists != 1)
            return null;

        var result = new Dictionary<int, StoredScript>();
        cmd.SetCommandAndParameters(@"
            SELECT ""Version"", ""DowngradeScript"", ""WriterVersion""
            FROM ""SchemaScript""
        ");
        await using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await rd.ReadAsync().ConfigureAwait(false))
            result[rd.GetInt32(0)] = new StoredScript(rd.IsDBNull(1) ? null : rd.GetString(1), rd.GetInt32(2));

        return result;
    }

    /// <summary>
    /// Applies the downgrade scripts to the database.
    /// For each version, a script stored in the database by a newer release is preferred,
    /// then the script bundled with this release, then any script stored in the database.
    /// </summary>
    /// <param name="db">The database path</param>
    /// <param name="dbversion">The current database version</param>
    /// <param name="targetversion">The target database version</param>
    /// <param name="bundled">The scripts bundled with this release</param>
    /// <param name="nobackups">Flag to disable backups</param>
    /// <returns>A task that completes when the downgrade is done.</returns>
    public static async Task ApplyDowngradeAsync(string db, int dbversion, int targetversion, SchemaScriptSet bundled, bool nobackups)
    {
        if (dbversion > targetversion)
        {
            var stored = await ReadStoredScriptsAsync(db).ConfigureAwait(false);
            var latestBundled = bundled.LatestVersion;

            var steps = new List<DowngradeStep>();
            var missing = new List<int>();
            var forbidden = new List<int>();
            for (var v = dbversion; v > targetversion; v--)
            {
                StoredScript? row = null;
                stored?.TryGetValue(v, out row);
                var bundledFile = bundled.Downgrades.FirstOrDefault(x => x.Version == v);

                DowngradeStep? step;
                if (row != null && row.WriterVersion > latestBundled)
                {
                    // A newer release wrote this row; a null script means it cannot be downgraded
                    step = row.Content == null
                        ? null
                        : new DowngradeStep(v, $"for version {v} from the database (written by schema version {row.WriterVersion})", row.Content, true);
                }
                else if (bundledFile != null)
                    step = new DowngradeStep(v, bundledFile.Filename, bundled.GetDowngradeScript(v)!, false);
                else if (row?.Content != null)
                    step = new DowngradeStep(v, $"for version {v} from the database (written by schema version {row.WriterVersion})", row.Content, true);
                else
                    step = null;

                if (step == null)
                    missing.Add(v);
                else if (step.FromDatabase && ForbiddenStoredSql.IsMatch(step.Content))
                    forbidden.Add(v);
                else
                    steps.Add(step);
            }

            if (missing.Count > 0)
            {
                Console.WriteLine($"Missing downgrade scripts for versions: {string.Join(", ", missing)}");
                return;
            }

            if (forbidden.Count > 0)
            {
                Console.WriteLine($"The downgrade scripts stored in the database for versions {string.Join(", ", forbidden)} use ATTACH, DETACH, VACUUM or load_extension, which is not allowed");
                return;
            }

            Console.WriteLine($"Downgrading {db} from version {dbversion} to {targetversion}");
            if (!nobackups)
                Helper.CreateFileBackup(db);

            await using var con = await SQLiteLoader.LoadConnectionAsync(db)
                .ConfigureAwait(false);
            await using var tr = con.BeginTransaction();
            await using var cmd = con.CreateCommand(tr);
            foreach (var step in steps)
            {
                Console.WriteLine($"Applying downgrade script {step.Description} ...");
                try
                {
                    cmd.SetCommandAndParameters(step.Content);
                    var r = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    if (r != null)
                        throw new UserInformationException($"{r}", "DowngradeScriptFailure");

                    await cmd.SetCommandAndParameters(@"
                        UPDATE ""Version""
                        SET ""Version"" = @Version
                    ")
                        .SetParameterValue("@Version", step.Version - 1)
                        .ExecuteNonQueryAsync()
                        .ConfigureAwait(false);

                    if (stored != null)
                        await cmd.SetCommandAndParameters(@"
                            DELETE FROM ""SchemaScript""
                            WHERE ""Version"" >= @Version
                        ")
                            .SetParameterValue("@Version", step.Version)
                            .ExecuteNonQueryAsync()
                            .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new UserInformationException($"Failed applying downgrade script {step.Description}: {ex.Message}", "DowngradeScriptFailure", ex);
                }
            }

            await tr.CommitAsync().ConfigureAwait(false);
            Console.WriteLine($"Downgraded {db} to version {targetversion}");
        }
        else if (dbversion == targetversion)
        {
            Console.WriteLine($"Database {db} is already at version {targetversion}");
        }
        else
        {
            Console.WriteLine($"Database {db} is already at version {dbversion} (cannot downgrade to {targetversion})");
        }
    }
}
