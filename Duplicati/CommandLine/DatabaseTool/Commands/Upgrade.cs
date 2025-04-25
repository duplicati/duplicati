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
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;

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
    public static Command Create() =>
        new Command("upgrade", "Upgrades one or more databases to a larger version")
        {
            new Argument<string[]>("databases", "The databases to upgrade") {
                Arity = ArgumentArity.ZeroOrMore
            },
            new Option<DirectoryInfo>("--server-datafolder", description: "The folder with databases", getDefaultValue: () => new DirectoryInfo(DataFolderLocator.GetDefaultStorageFolder(DataFolderManager.SERVER_DATABASE_FILENAME, false, true))),
            new Option<int>("--server-version", description: "The version to upgrade the server database to; zero or less means latest", getDefaultValue: () => 0),
            new Option<int>("--local-version", description: "The version to upgrade local databases to; zero or less means latest", getDefaultValue: () => 0),
            new Option<bool>("--no-backups", description: "Do not create backups before upgrade", getDefaultValue: () => false),
            new Option<bool>("--include-untracked-databases", description: "Include untracked databases in the upgrade process", getDefaultValue: () => false)
        }
        .WithHandler(CommandHandler.Create<string[], DirectoryInfo, int, int, bool, bool>((databases, serverdatafolder, serverversion, localversion, nobackups, includeuntrackeddatabases) =>
            {
                databases = Helper.FindAllDatabases(databases, serverdatafolder.FullName, includeuntrackeddatabases);
                if (databases.Length == 0)
                {
                    Console.WriteLine("No databases found to upgrade");
                    return;
                }

                var serverVersions = ExtractScriptsFromAssembly(typeof(Library.RestAPI.Database.DatabaseSchemaMarker));
                var localVersions = ExtractScriptsFromAssembly(typeof(Library.Main.Database.DatabaseSchemaMarker));

                if (serverversion <= 0)
                    serverversion = serverVersions.Max(x => x.Version);
                if (localversion <= 0)
                    localversion = localVersions.Max(x => x.Version);

                if (serverversion > serverVersions.Max(x => x.Version))
                    throw new UserInformationException($"Server version {serverversion} is greater than the latest version {serverVersions.Max(x => x.Version)}", "UnsupportedUpgradeVersion");
                if (localversion > localVersions.Max(x => x.Version))
                    throw new UserInformationException($"Local version {localversion} is greater than the latest version {localVersions.Max(x => x.Version)}", "UnsupportedUpgradeVersion");

                foreach (var db in databases)
                {
                    Console.WriteLine($"Examining {db} ...");
                    if (!File.Exists(db))
                    {
                        Console.WriteLine($"Database {db} does not exist");
                        continue;
                    }

                    int version;
                    bool isserverdb;
                    try
                    {
                        (version, isserverdb) = Helper.ExamineDatabase(db);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading database {db}: {ex.Message}");
                        continue;
                    }

                    Console.WriteLine($"Database {db} is version {version} and is a {(isserverdb ? "server" : "local")} database");
                    ApplyUpgrade(db, version,
                        isserverdb ? serverversion : localversion,
                        isserverdb ? serverVersions : localVersions,
                    nobackups);

                }
            }));

    /// <summary>
    /// Finds all upgrade scripts in the assembly
    /// </summary>
    /// <param name="markerType">The type to use as a marker for the assembly</param>
    /// <returns>The list of upgrade scripts</returns>
    private static IEnumerable<UpgradeScript> ExtractScriptsFromAssembly(Type markerType)
    {
        string ReadStream(string path)
        {
            using var reader = new StreamReader(markerType.Assembly.GetManifestResourceStream(path) ?? throw new InvalidOperationException());
            return reader.ReadToEnd();
        }

        static string? ResourceNameToPath(string nsprefix, string resourcename)
        {
            if (resourcename.StartsWith(nsprefix))
                return resourcename.Substring(nsprefix.Length);

            return null;
        }

        return markerType.Assembly.GetManifestResourceNames()
            .Select(x => (ResourceName: x, Filename: ResourceNameToPath(markerType.Namespace + ".Database_schema.", x)))
            .Where(x => x.Filename != null && x.Filename.EndsWith(".sql"))
            .Where(x => x.Filename != "Schema.sql")
            .Select(x => new UpgradeScript(Version: int.Parse(x.Filename!.Split('.').First()), Filename: x.Filename!, Content: ReadStream(x.ResourceName)))
            .ToList();
    }

    /// <summary>
    /// The upgrade script
    /// </summary>
    /// <param name="Filename">The path to the assembly resource</param>
    /// <param name="Version">The script version</param>
    /// <param name="Content">The script contents</param>
    private sealed record UpgradeScript(string Filename, int Version, string Content);

    /// <summary>
    /// Applies the upgrade scripts to the database
    /// </summary>
    /// <param name="db">The database path</param>
    /// <param name="dbversion">The current database version</param>
    /// <param name="targetversion">The target database version</param>
    /// <param name="scripts">The upgrade scripts</param>
    /// <param name="nobackups">Flag to disable backups</param>
    private static void ApplyUpgrade(string db, int dbversion, int targetversion, IEnumerable<UpgradeScript> scripts, bool nobackups)
    {
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

            using var con = SQLiteLoader.LoadConnection(db);
            using var tr = con.BeginTransaction();
            using var cmd = con.CreateCommand(tr);
            foreach (var script in upgradeScripts)
            {
                Console.WriteLine($"Applying upgrade script {script.Filename} ...");
                try
                {
                    cmd.SetCommandAndParameters(script.Content);
                    var r = cmd.ExecuteScalar();
                    if (r != null)
                        throw new UserInformationException($"{r}", "UpgradeScriptFailure");
                    cmd.SetCommandAndParameters("UPDATE Version SET Version = @Version")
                        .SetParameterValue("@Version", script.Version);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    throw new UserInformationException($"Failed applying upgrade script {script.Filename}: {ex.Message}", "DowngradeScriptFailure", ex);
                }
            }

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
