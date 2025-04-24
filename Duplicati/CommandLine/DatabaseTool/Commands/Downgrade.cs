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
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;

namespace Duplicati.CommandLine.DatabaseTool.Commands;

/// <summary>
/// The downgrade command
/// </summary>
public static class Downgrade
{
    /// <summary>
    /// Creates the downgrade command
    /// </summary>
    /// <returns>The downgrade command</returns>
    public static Command Create() =>
        new Command("downgrade", "Downgrades one or more databases to a prior version")
        {
            new Argument<string[]>("databases", "The databases to downgrade") {
                Arity = ArgumentArity.ZeroOrMore
            },
            new Option<DirectoryInfo>("--server-datafolder", description: "The folder with databases", getDefaultValue: () => new DirectoryInfo(DataFolderLocator.GetDefaultStorageFolder(DataFolderManager.SERVER_DATABASE_FILENAME, false, true))),
            new Option<int>("--server-version", description: "The version to downgrade the server database to", getDefaultValue: () => 6),
            new Option<int>("--local-version", description: "The version to downgrade local databases to", getDefaultValue: () => 12),
            new Option<bool>("--no-backups", description: "Do not create backups before downgrade", getDefaultValue: () => false),
        }
        .WithHandler(CommandHandler.Create<string[], DirectoryInfo, int, int, bool>((databases, serverdatafolder, serverversion, localversion, nobackups) =>
            {
                databases ??= [];
                if (databases.Length == 0)
                {
                    // No explicit paths given, so we find all databases in the folder
                    var serverdb = Path.Combine(serverdatafolder.FullName, DataFolderManager.SERVER_DATABASE_FILENAME);
                    var dbpaths =
                        CLIDatabaseLocator.GetAllDatabasePaths()
                        .Prepend(serverdb)
                        .ToList();

                    // Append any database paths from the server database
                    if (File.Exists(serverdb))
                    {
                        try
                        {
                            using var con = SQLiteLoader.LoadConnection(serverdb);
                            using var cmd = con.CreateCommand();
                            foreach (var rd in cmd.ExecuteReaderEnumerable(@"SELECT ""DBPath"" FROM ""Backup"""))
                                dbpaths.Add(rd.ConvertValueToString(0) ?? "");
                        }
                        catch
                        {
                        }
                    }

                    databases = dbpaths
                        .Where(x => File.Exists(x))
                        .Distinct()
                        .ToArray();
                }

                if (databases.Length == 0)
                {
                    Console.WriteLine("No databases found to downgrade");
                    return;
                }

                static string ReadStream(string path)
                {
                    using var reader = new StreamReader(typeof(Downgrade).Assembly.GetManifestResourceStream(path) ?? throw new InvalidOperationException());
                    return reader.ReadToEnd();
                }

                static string ResourceNameToPath(string resourcename)
                {
                    var prefixes = new[] {
                        typeof(Program).Namespace + ".Scripts.Local.",
                        typeof(Program).Namespace + ".Scripts.Server."
                    };

                    foreach (var prefix in prefixes)
                        if (resourcename.StartsWith(prefix))
                            return resourcename.Substring(prefix.Length);

                    throw new Exception($"Unexpected resource name: {resourcename}");
                }

                var serverVersions = typeof(Downgrade).Assembly.GetManifestResourceNames()
                    .Where(x => x.Contains(".Scripts.Server."))
                    .Select(x => new DowngradeScript(Version: int.Parse(ResourceNameToPath(x).Split('.').First()), Filename: ResourceNameToPath(x), Content: ReadStream(x)))
                    .ToList();

                var localVersions = typeof(Downgrade).Assembly.GetManifestResourceNames()
                    .Where(x => x.Contains(".Scripts.Local."))
                    .Select(x => new DowngradeScript(Version: int.Parse(ResourceNameToPath(x).Split('.').First()), Filename: ResourceNameToPath(x), Content: ReadStream(x)))
                    .ToList();

                foreach (var db in databases)
                {
                    Console.WriteLine($"Examining {db} ...");
                    if (!File.Exists(db))
                    {
                        Console.WriteLine($"Database {db} does not exist");
                        continue;
                    }

                    try
                    {
                        bool isserverdb;
                        int version;
                        using (var con = SQLiteLoader.LoadConnection(db))
                        {
                            using var cmd = con.CreateCommand();
                            isserverdb = cmd.ExecuteScalarInt64("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Backup' OR name='Schedule'") == 2;
                            version = (int)cmd.ExecuteScalarInt64("SELECT MAX(Version) FROM Version");
                        }

                        Console.WriteLine($"Database {db} is version {version} and is a {(isserverdb ? "server" : "local")} database");
                        ApplyDowngrade(db, version,
                            isserverdb ? serverversion : localversion,
                            isserverdb ? serverVersions : localVersions,
                        nobackups);

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading database {db}: {ex.Message}");
                        continue;
                    }
                }
            }));

    /// <summary>
    /// The downgrade script
    /// </summary>
    /// <param name="Filename">The path to the assembly resource</param>
    /// <param name="Version">The script version</param>
    /// <param name="Content">The script contents</param>
    private sealed record DowngradeScript(string Filename, int Version, string Content);

    /// <summary>
    /// Applies the downgrade scripts to the database
    /// </summary>
    /// <param name="db">The database path</param>
    /// <param name="dbversion">The current database version</param>
    /// <param name="targetversion">The target database version</param>
    /// <param name="scripts">The downgrade scripts</param>
    /// <param name="nobackups">Flag to disable backups</param>
    private static void ApplyDowngrade(string db, int dbversion, int targetversion, IEnumerable<DowngradeScript> scripts, bool nobackups)
    {
        if (dbversion > targetversion)
        {
            var requiredVersions = Enumerable.Range(targetversion + 1, dbversion - targetversion)
                .Reverse()
                .ToList();

            var missingScripts = requiredVersions
                .Where(x => !scripts.Any(y => y.Version == x))
                .ToList();

            if (missingScripts.Count > 0)
            {
                Console.WriteLine($"Missing downgrade scripts for versions: {string.Join(", ", missingScripts)}");
                return;
            }

            var downgradeScripts = requiredVersions
                .Select(x => scripts.First(y => y.Version == x))
                .ToList();

            Console.WriteLine($"Downgrading {db} from version {dbversion} to {targetversion}");
            if (!nobackups)
                Helper.CreateFileBackup(db);

            using var con = SQLiteLoader.LoadConnection(db);
            using var tr = con.BeginTransaction();
            using var cmd = con.CreateCommand(tr);
            foreach (var script in downgradeScripts)
            {
                Console.WriteLine($"Applying downgrade script {script.Filename} ...");
                try
                {
                    cmd.SetCommandAndParameters(script.Content);
                    var r = cmd.ExecuteScalar();
                    if (r != null)
                        throw new UserInformationException($"{r}", "DowngradeScriptFailure");
                    cmd.SetCommandAndParameters("UPDATE Version SET Version = @Version")
                        .SetParameterValue("@Version", script.Version - 1);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    throw new UserInformationException($"Failed applying downgrade script {script.Filename}: {ex.Message}", "DowngradeScriptFailure", ex);
                }
            }

            tr.Commit();
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
