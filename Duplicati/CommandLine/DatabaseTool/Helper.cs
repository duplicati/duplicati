using System.Data;
using System.Text.Json;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;

namespace Duplicati.CommandLine.DatabaseTool;

public static class Helper
{
    /// <summary>
    /// Creates a backup of the file
    /// </summary>
    /// <param name="filename">The filename to backup</param>
    public static void CreateFileBackup(string path)
    {
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path) ?? "";
        var filename = Path.GetFileNameWithoutExtension(path);

        var newname = $"{filename}-{DateTime.Now:yyyMMddhhmmss}.bak";
        var backup = Path.Combine(dir, newname);
        var retry = 0;
        while (File.Exists(backup))
        {
            if (retry > 100)
                throw new IOException($"Cannot create backup file {backup} - too many retries");

            retry++;
            newname = $"{filename}-{DateTime.Now:yyyMMddhhmmss}-{retry}.bak";
            backup = Path.Combine(dir, newname);
        }

        File.Copy(path, backup);
    }

    /// <summary>
    /// Find all databases in the data folder
    /// </summary>
    /// <param name="databases">The input databases</param>
    /// <param name="datafolder">The folder to scan</param>
    /// <param name="scanExtra">Whether to scan for extra databases</param>
    /// <returns>The list of databases</returns>
    public static async Task<string[]> FindAllDatabases(string[]? databases, string datafolder, bool scanExtra)
    {
        databases ??= [];
        if (databases.Length != 0)
            return databases;

        // No explicit paths given, so we find all databases in the folder
        var serverdb = Path.Combine(datafolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbpaths =
            CLIDatabaseLocator.GetAllDatabasePaths()
            .Prepend(serverdb)
            .ToList();

        // Append any database paths from the server database
        if (File.Exists(serverdb))
        {
            try
            {
                using var con = await SQLiteLoader.LoadConnectionAsync(serverdb, 0)
                    .ConfigureAwait(false);
                using var cmd = con.CreateCommand(@"
                    SELECT ""DBPath""
                    FROM ""Backup""
                ");
                await foreach (var rd in cmd.ExecuteReaderEnumerableAsync().ConfigureAwait(false))
                    dbpaths.Add(rd.ConvertValueToString(0) ?? "");
            }
            catch
            {
            }
        }

        if (scanExtra)
            dbpaths.AddRange(
                Directory.EnumerateFiles(datafolder, "*.sqlite", SearchOption.AllDirectories)
                .Where(x => CLIDatabaseLocator.IsRandomlyGeneratedName(x))
                .Select(x => Path.GetFullPath(x)));

        return dbpaths
            .Where(x => File.Exists(x))
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Examines the database and returns the version and whether it is a server database
    /// </summary>
    /// <param name="db">The path to the database</param>
    /// <returns>A tuple containing the version and whether it is a server database</returns>
    public static async Task<(int Version, bool isserver)> ExamineDatabase(string db)
    {
        using (var con = await SQLiteLoader.LoadConnectionAsync(db, 0).ConfigureAwait(false))
        {
            using var cmd = con.CreateCommand();

            var isserverdb = await cmd.ExecuteScalarInt64Async(@"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE
                    type='table'
                    AND name='Backup'
                    OR name='Schedule'
            ")
                .ConfigureAwait(false) == 2;

            var version = (int)await cmd.ExecuteScalarInt64Async(@"
                SELECT MAX(Version)
                FROM Version
            ")
                .ConfigureAwait(false);

            return (version, isserverdb);
        }
    }

    /// <summary>
    /// Prints the data reader to the console
    /// </summary>
    /// <param name="reader">The data reader to print</param>
    /// <param name="useJson">Whether to use JSON or not</param>
    public static void Print(this IDataReader reader, bool useJson)
    {
        if (useJson)
            PrintJson(reader);
        else
            PrintReader(reader);
    }

    /// <summary>
    /// Prints the data reader to the console in JSON format
    /// </summary>
    /// <param name="reader">The data reader to print</param>
    public static void PrintJson(this IDataReader reader)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();

        // Write the header row
        writer.WriteStartArray();
        for (int i = 0; i < reader.FieldCount; i++)
            writer.WriteStringValue(reader.GetName(i));
        writer.WriteEndArray();

        // Write each data row
        while (reader.Read())
        {
            writer.WriteStartArray();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                if (value == DBNull.Value)
                    writer.WriteNullValue();
                else if (value is string s)
                    writer.WriteStringValue(s);
                else if (value is int i32)
                    writer.WriteNumberValue(i32);
                else if (value is long i64)
                    writer.WriteNumberValue(i64);
                else if (value is double d)
                    writer.WriteNumberValue(d);
                else if (value is float f)
                    writer.WriteNumberValue(f);
                else if (value is bool b)
                    writer.WriteBooleanValue(b);
                else if (value is DateTime dt)
                    writer.WriteStringValue(dt); // ISO 8601 format
                else
                    writer.WriteStringValue(value.ToString()); // Fallback
            }
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.Flush();
    }

    /// <summary>
    /// Prints the data reader to the console in a tabular format
    /// </summary>
    /// <param name="reader">The data reader to print</param>
    public static void PrintReader(this IDataReader reader)
    {
        // Print the header row
        for (int i = 0; i < reader.FieldCount; i++)
            Console.Write($"{reader.GetName(i)}\t");
        Console.WriteLine();

        // Print each data row
        while (reader.Read())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                if (value == DBNull.Value)
                    Console.Write("<null>\t");
                else
                    Console.Write($"{value}\t");
            }
            Console.WriteLine();
        }
    }
}