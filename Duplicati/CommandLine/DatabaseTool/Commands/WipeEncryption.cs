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
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Encryption;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using Microsoft.Data.Sqlite;

namespace Duplicati.CommandLine.DatabaseTool.Commands;

/// <summary>
/// The wipe-encryption command - clears any encrypted strings from the server database so
/// that Duplicati can start with a clean state and let the user re-enter credentials.
/// Only applies to the server database; backup and sync databases are skipped.
/// </summary>
public static class WipeEncryption
{
    /// <summary>
    /// Creates the wipe-encryption command
    /// </summary>
    /// <returns>The wipe-encryption command</returns>
    public static Command Create() =>
        new Command("wipe-encryption", "Removes or clears any encrypted strings from the server database so it can be used without the original encryption key. A backup of the database is created first unless --no-backups is given. Only applies to the server database; other databases are skipped.")
        {
            new Argument<string[]>("databases", "The databases to wipe encryption from") {
                Arity = ArgumentArity.ZeroOrMore
            },
            new Option<DirectoryInfo>("--server-datafolder", description: "The folder with databases", getDefaultValue: () => new DirectoryInfo(DataFolderLocator.GetDefaultStorageFolder(DataFolderManager.SERVER_DATABASE_FILENAME, false, true))),
            new Option<bool>("--no-backups", description: "Do not create a backup before wiping", getDefaultValue: () => false),
            new Option<bool>("--include-untracked-databases", description: "Include untracked databases in the wipe process", getDefaultValue: () => false),
            new Option<bool>("--dry-run", description: "Show what would be wiped without making changes", getDefaultValue: () => false),
        }
        .WithHandler(CommandHandler.Create<string[], DirectoryInfo, bool, bool, bool>(async (databases, serverdatafolder, nobackups, includeuntrackeddatabases, dryrun) =>
            {
                databases = await Helper.FindAllDatabasesAsync(databases, serverdatafolder.FullName, includeuntrackeddatabases);
                if (databases.Length == 0)
                {
                    Console.WriteLine("No databases found to wipe encryption from");
                    return;
                }

                long totalWiped = 0;
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
                        (version, type) = await Helper.ExamineDatabaseAsync(db);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading database {db}: {ex.Message}");
                        continue;
                    }

                    Console.WriteLine($"Database {db} is version {version} and is a {type} database");

                    if (type != DatabaseType.Server)
                    {
                        Console.WriteLine($"Skipping {db}: wipe-encryption only applies to server databases, not {type} databases");
                        continue;
                    }

                    if (!nobackups && !dryrun)
                        Helper.CreateFileBackup(db);

                    var wiped = await WipeServerDatabaseAsync(db, dryrun);

                    Console.WriteLine($"Wiped {wiped} encrypted field(s) from {db}");
                    totalWiped += wiped;
                }

                if (dryrun)
                    Console.WriteLine($"Dry run complete: {totalWiped} encrypted field(s) would be wiped. No changes were made.");
                else
                    Console.WriteLine($"Wipe complete: {totalWiped} encrypted field(s) wiped.");
            }));

    /// <summary>
    /// Wipes encrypted fields from a server database. Encrypted values are identified by
    /// the <see cref="EncryptedFieldHelper.HEADER_PREFIX"/> ("enc-v1:") prefix and replaced
    /// with an empty string. The server-wide <c>encrypted-fields</c> flag is also cleared so
    /// Duplicati treats the database as unencrypted on next start.
    /// </summary>
    /// <param name="db">The path to the server database.</param>
    /// <param name="dryRun">If <c>true</c>, only count the fields that would be wiped.</param>
    /// <returns>The number of encrypted field values that were (or would be) wiped.</returns>
    private static async Task<long> WipeServerDatabaseAsync(string db, bool dryRun)
    {
        // Columns that may hold an enc-v1: encrypted value in the server schema.
        // The Option.Value column is wiped separately by name (see WipePasswordOptionsAsync)
        // because only known password option names carry encrypted values.
        var targets = new[]
        {
            new { Table = "Backup", Column = "TargetURL" },
            new { Table = "Source", Column = "Path" },
            new { Table = "ConnectionString", Column = "BaseUrl" },
            new { Table = "BackupTargetUrl", Column = "TargetURL" },
        };

        long total = 0;
        await using var con = await SQLiteLoader.LoadConnectionAsync(db);
        await using var tr = dryRun ? null : con.BeginTransaction();

        // Wipe whole-column encrypted values (TargetURL, Source.Path, etc.)
        foreach (var t in targets)
            total += await WipeColumnAsync(con, tr, t.Table, t.Column);

        // Wipe Option.Value rows whose Name is a known password field and whose Value
        // carries the encryption prefix. Non-password options (e.g. --dblock-size) are
        // left intact so the backup configuration stays usable.
        total += await WipePasswordOptionsAsync(con, tr, Connection.PasswordFieldNames);

        if (!dryRun)
        {
            // Clear the server-wide "encrypted-fields" flag so Duplicati starts up treating
            // the database as unencrypted and will re-encrypt (or leave plaintext) on next save.
            using var flagCmd = con.CreateCommand(tr!)
                .SetCommandAndParameters(@"
                    UPDATE ""Option""
                    SET ""Value"" = 'False'
                    WHERE ""BackupID"" = @BackupId AND ""Name"" = @Name
                ")
                .SetParameterValue("@BackupId", Connection.SERVER_SETTINGS_ID)
                .SetParameterValue("@Name", ServerSettings.CONST.ENCRYPTED_FIELDS);
            await flagCmd.ExecuteNonQueryAsync(CancellationToken.None);

            tr!.Commit();

            // Reclaim space freed by the wiped values.
            using var vacuumCmd = con.CreateCommand().SetCommandAndParameters("VACUUM");
            await vacuumCmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        return total;
    }

    /// <summary>
    /// Counts and (unless <paramref name="transaction"/> is <c>null</c>) clears every row in
    /// <c>"<paramref name="table"/>"."<paramref name="column"/>"</c> whose value starts with
    /// the encryption header prefix. Tables that do not exist in the database (e.g. older
    /// schema versions) are skipped silently.
    /// </summary>
    /// <param name="con">The database connection.</param>
    /// <param name="transaction">The open transaction, or <c>null</c> for a dry run.</param>
    /// <param name="table">The table name.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The number of rows that were (or would be) wiped.</returns>
    private static async Task<long> WipeColumnAsync(SqliteConnection con, SqliteTransaction? transaction, string table, string column)
    {
        // Skip tables that are not present in this schema version (e.g. ConnectionString
        // and BackupTargetUrl were added after the v6 server schema captured in tests).
        using (var existsCmd = con.CreateCommand(transaction!).SetCommandAndParameters(
            @"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@Name")
            .SetParameterValue("@Name", table))
        {
            if (await existsCmd.ExecuteScalarInt64Async(CancellationToken.None) == 0)
                return 0;
        }

        var like = EncryptedFieldHelper.HEADER_PREFIX + "%";

        using var countCmd = con.CreateCommand(transaction!).SetCommandAndParameters($@"SELECT COUNT(*) FROM ""{table}"" WHERE ""{column}"" LIKE @Like")
            .SetParameterValue("@Like", like);
        var count = await countCmd.ExecuteScalarInt64Async(CancellationToken.None);

        if (transaction != null && count > 0)
        {
            using var wipeCmd = con.CreateCommand(transaction).SetCommandAndParameters($@"UPDATE ""{table}"" SET ""{column}"" = '' WHERE ""{column}"" LIKE @Like")
                .SetParameterValue("@Like", like);
            await wipeCmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        return count;
    }

    /// <summary>
    /// Counts and (unless <paramref name="transaction"/> is <c>null</c>) clears every
    /// <c>Option.Value</c> whose <c>Name</c> is a known password field and whose value is
    /// encrypted. Non-password options are left intact.
    /// </summary>
    /// <param name="con">The database connection.</param>
    /// <param name="transaction">The open transaction, or <c>null</c> for a dry run.</param>
    /// <param name="passwordFields">The set of option names whose values are sensitive.</param>
    /// <returns>The number of option rows that were (or would be) wiped.</returns>
    private static async Task<long> WipePasswordOptionsAsync(SqliteConnection con, SqliteTransaction? transaction, IReadOnlySet<string> passwordFields)
    {
        if (passwordFields.Count == 0)
            return 0;

        var like = EncryptedFieldHelper.HEADER_PREFIX + "%";

        // The password-field set is dynamic (depends on loaded modules) but is small enough
        // to build a parameterized IN clause. The set is matched case-insensitively in Connection,
        // and SQLite's LIKE is case-insensitive for ASCII text by default.
        var paramNames = passwordFields.Select((_, i) => $"@p{i}").ToArray();
        var inClause = string.Join(",", paramNames);

        var countCmd = con.CreateCommand(transaction!).SetCommandAndParameters($@"SELECT COUNT(*) FROM ""Option"" WHERE ""Name"" IN ({inClause}) AND ""Value"" LIKE @Like")
            .SetParameterValue("@Like", like);
        var idx = 0;
        foreach (var name in passwordFields)
            countCmd.SetParameterValue(paramNames[idx++], name);
        using (countCmd)
        {
            var count = await countCmd.ExecuteScalarInt64Async(CancellationToken.None);
            if (transaction == null || count == 0)
                return count;

            using var wipeCmd = con.CreateCommand(transaction).SetCommandAndParameters($@"UPDATE ""Option"" SET ""Value"" = '' WHERE ""Name"" IN ({inClause}) AND ""Value"" LIKE @Like")
                .SetParameterValue("@Like", like);
            idx = 0;
            foreach (var name in passwordFields)
                wipeCmd.SetParameterValue(paramNames[idx++], name);
            await wipeCmd.ExecuteNonQueryAsync(CancellationToken.None);

            return count;
        }
    }
}
