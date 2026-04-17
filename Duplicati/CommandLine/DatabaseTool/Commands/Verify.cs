
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
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;

namespace Duplicati.CommandLine.DatabaseTool.Commands;

/// <summary>
/// Database status information
/// </summary>
public class DatabaseStatus
{
    /// <summary>
    /// The path to the database
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The status of the database: Found, Missing, or Orphaned
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// Source of the reference (dbconfig.json, server database, or filesystem)
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Whether the database file exists on disk
    /// </summary>
    public bool FileExists { get; set; }

    /// <summary>
    /// Whether the database is referenced in dbconfig.json
    /// </summary>
    public bool InDbConfig { get; set; }

    /// <summary>
    /// Whether the database is referenced in the server database
    /// </summary>
    public bool InServerDb { get; set; }
}

/// <summary>
/// The verify command - lists all databases with their status
/// </summary>
public static class Verify
{
    /// <summary>
    /// The filename of the file with database configurations
    /// </summary>
    private const string CONFIG_FILE = "dbconfig.json";

    /// <summary>
    /// Creates the verify command
    /// </summary>
    /// <returns>The verify command</returns>
    public static Command Create() =>
        new Command("verify", "Verifies database files and shows their status (Found, Missing, Orphaned)")
        {
            new Option<DirectoryInfo>("--datafolder", description: "The folder with databases", getDefaultValue: () => new DirectoryInfo(DataFolderLocator.GetDefaultStorageFolder(DataFolderManager.SERVER_DATABASE_FILENAME, false, true))),
            new Option<bool>("--output-json", description: "Output as JSON", getDefaultValue: () => false),
            new Option<bool>("--include-server", description: "Include server database in the list", getDefaultValue: () => true),
        }
        .WithHandler(CommandHandler.Create<DirectoryInfo, bool, bool>(async (datafolder, outputjson, includeserver) =>
        {
            var datafolderPath = datafolder.FullName;
            var results = await AnalyzeDatabasesAsync(datafolderPath, includeserver);

            if (outputjson)
            {
                Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                PrintResults(results, includeserver);
            }
        }));

    /// <summary>
    /// Analyzes all databases and returns their status
    /// </summary>
    public static async Task<List<DatabaseStatus>> AnalyzeDatabasesAsync(string datafolder, bool includeServer)
    {
        var results = new List<DatabaseStatus>();
        var serverDbPath = Path.Combine(datafolder, DataFolderManager.SERVER_DATABASE_FILENAME);

        // Get databases referenced in dbconfig.json
        var dbConfigPath = Path.Combine(datafolder, CONFIG_FILE);
        var dbConfigPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(dbConfigPath))
        {
            try
            {
                var configs = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CLIDatabaseLocator.BackendEntry>>(
                    await File.ReadAllTextAsync(dbConfigPath));
                if (configs != null)
                {
                    foreach (var config in configs)
                    {
                        if (!string.IsNullOrEmpty(config.Databasepath))
                            dbConfigPaths.Add(Path.GetFullPath(config.Databasepath));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse {CONFIG_FILE}: {ex.Message}");
            }
        }

        // Get databases referenced in server database
        var serverDbPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(serverDbPath))
        {
            try
            {
                await using var con = await SQLiteLoader.LoadConnectionAsync(serverDbPath);
                await using var cmd = con.CreateCommand(@"
                    SELECT ""DBPath""
                    FROM ""Backup""
                ");
                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync(CancellationToken.None))
                {
                    var path = rd.ConvertValueToString(0);
                    if (!string.IsNullOrEmpty(path))
                        serverDbPaths.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not read server database: {ex.Message}");
            }
        }

        // Get all sqlite files in the datafolder
        var fileSystemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(datafolder))
        {
            foreach (var file in Directory.EnumerateFiles(datafolder, "*.sqlite", SearchOption.AllDirectories))
            {
                fileSystemPaths.Add(Path.GetFullPath(file));
            }
        }

        // Combine all unique paths
        var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(dbConfigPaths);
        allPaths.UnionWith(serverDbPaths);
        allPaths.UnionWith(fileSystemPaths);

        // Analyze each path
        foreach (var path in allPaths)
        {
            // Skip server database unless included
            if (!includeServer && Path.GetFileName(path).Equals(DataFolderManager.SERVER_DATABASE_FILENAME, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileExists = File.Exists(path);
            var inDbConfig = dbConfigPaths.Contains(path);
            var inServerDb = serverDbPaths.Contains(path);

            string status;
            string source;

            if (fileExists && (inDbConfig || inServerDb))
            {
                status = "Found";
                var sources = new List<string>();
                if (inDbConfig) sources.Add("dbconfig.json");
                if (inServerDb) sources.Add("server");
                source = string.Join(", ", sources);
            }
            else if (!fileExists && (inDbConfig || inServerDb))
            {
                status = "Missing";
                var sources = new List<string>();
                if (inDbConfig) sources.Add("dbconfig.json");
                if (inServerDb) sources.Add("server");
                source = string.Join(", ", sources);
            }
            else if (fileExists && !inDbConfig && !inServerDb)
            {
                status = "Orphaned";
                source = "filesystem only";
            }
            else
            {
                continue; // Should not happen
            }

            results.Add(new DatabaseStatus
            {
                Path = path,
                Status = status,
                Source = source,
                FileExists = fileExists,
                InDbConfig = inDbConfig,
                InServerDb = inServerDb
            });
        }

        return results.OrderBy(r => r.Status).ThenBy(r => r.Path).ToList();
    }

    /// <summary>
    /// Gets orphaned databases only
    /// </summary>
    public static async Task<List<DatabaseStatus>> GetOrphanedDatabasesAsync(string datafolder)
    {
        var all = await AnalyzeDatabasesAsync(datafolder, true);
        return all.Where(d => d.Status == "Orphaned").ToList();
    }

    /// <summary>
    /// Prints the results to the console
    /// </summary>
    private static void PrintResults(List<DatabaseStatus> results, bool includeServer)
    {
        if (results.Count == 0)
        {
            Console.WriteLine("No databases found.");
            return;
        }

        // Group by status
        var found = results.Where(r => r.Status == "Found").ToList();
        var missing = results.Where(r => r.Status == "Missing").ToList();
        var orphaned = results.Where(r => r.Status == "Orphaned").ToList();

        Console.WriteLine($"Database Verification Results");
        Console.WriteLine($"==============================");
        Console.WriteLine($"Total: {results.Count} databases");
        Console.WriteLine($"  Found: {found.Count}");
        Console.WriteLine($"  Missing: {missing.Count}");
        Console.WriteLine($"  Orphaned: {orphaned.Count}");
        Console.WriteLine();

        if (found.Count > 0)
        {
            Console.WriteLine($"Found ({found.Count}):");
            Console.WriteLine(new string('-', 80));
            foreach (var db in found)
            {
                Console.WriteLine($"  {db.Path}");
                Console.WriteLine($"    Source: {db.Source}");
            }
            Console.WriteLine();
        }

        if (missing.Count > 0)
        {
            Console.WriteLine($"Missing ({missing.Count}):");
            Console.WriteLine(new string('-', 80));
            foreach (var db in missing)
            {
                Console.WriteLine($"  {db.Path}");
                Console.WriteLine($"    Expected in: {db.Source}");
            }
            Console.WriteLine();
        }

        if (orphaned.Count > 0)
        {
            Console.WriteLine($"Orphaned ({orphaned.Count}):");
            Console.WriteLine(new string('-', 80));
            foreach (var db in orphaned)
            {
                Console.WriteLine($"  {db.Path}");
                Console.WriteLine($"    Not referenced in dbconfig.json or server database");
            }
            Console.WriteLine();
        }
    }
}
