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

#nullable enable

using System;
using System.Globalization;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SQLiteHelper
{
    public static class SQLiteLoader
    {
        /// <summary>
        /// The minimum value for the SQLite page cache size
        /// </summary>
        public const long MINIMUM_SQLITE_PAGE_CACHE_SIZE = 2048000L;

        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(SQLiteLoader));

        public static void OpenDatabase(Microsoft.Data.Sqlite.SqliteConnection con, string databasePath, string? decryptionPassword)
        {
            OpenDatabaseAsync(con, databasePath, decryptionPassword).Await();
        }

        /// <summary>
        /// Helper method with logic to handle opening a database in possibly encrypted format
        /// </summary>
        /// <param name="con">The SQLite connection object</param>
        /// <param name="databasePath">The location of Duplicati's database.</param>
        /// <param name="decryptionPassword">The password to use for decryption.</param>
        public static async Task OpenDatabaseAsync(Microsoft.Data.Sqlite.SqliteConnection con, string databasePath, string? decryptionPassword)
        {
            if (!string.IsNullOrWhiteSpace(decryptionPassword) && SQLiteRC4Decrypter.IsDatabaseEncrypted(databasePath))
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "SQLiteRC4Decrypter", null, "Database is encrypted, attempting to decrypt...");
                try
                {
                    SQLiteRC4Decrypter.DecryptSQLiteFile(databasePath, decryptionPassword);
                    Logging.Log.WriteInformationMessage(LOGTAG, "SQLiteRC4Decrypter", "Database decrypted successfully.");
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "SQLiteRC4Decrypter", ex, "Failed to decrypt database");
                    throw new UserInformationException($"The database appears to be encrypted, but the decrypting failed. Please check the password. Error message: {ex.Message}", "RC4DecryptionFailed", ex);
                }
            }

            try
            {
                //Attempt to open in preferred state
                await OpenSQLiteFileAsync(con, databasePath);
                await TestSQLiteFileAsync(con);
            }
            catch
            {
                try { await con.DisposeAsync(); }
                catch { }

                throw;
            }

            if (con.State != System.Data.ConnectionState.Open)
                throw new UserInformationException("Failed to open database for unknown reason, check the logs to see error messages", "DatabaseOpenFailed");
        }

        public static Microsoft.Data.Sqlite.SqliteConnection LoadConnection()
        {
            return LoadConnectionAsync().Await();
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database
        /// </summary>
        /// <returns>The SQLite connection instance.</returns>
        public static async Task<Microsoft.Data.Sqlite.SqliteConnection> LoadConnectionAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection? con = null;
            SetEnvironmentVariablesForSQLiteTempDir();

            try
            {
                con = new Microsoft.Data.Sqlite.SqliteConnection();
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "FailedToLoadConnectionSQLite", ex, "Failed to load connection.");
                await DisposeConnectionAsync(con);

                throw;
            }

            return con ?? throw new InvalidOperationException("Failed to load connection");
        }

        public static Microsoft.Data.Sqlite.SqliteConnection ApplyCustomPragmas(Microsoft.Data.Sqlite.SqliteConnection con, long pagecachesize)
        {
            return ApplyCustomPragmasAsync(con, pagecachesize).Await();
        }

        /// <summary>
        /// Applies user-supplied custom pragmas to the SQLite connection
        /// </summary>
        /// <param name="con">The connection to apply the pragmas to.</param>
        /// <param name="pagecachesize"> The page cache size to set.</param>
        /// <returns>The connection with the pragmas applied.</returns>
        public static async Task<Microsoft.Data.Sqlite.SqliteConnection> ApplyCustomPragmasAsync(Microsoft.Data.Sqlite.SqliteConnection con, long pagecachesize)
        {
            // TODO more default custom options
            //await cmd.ExecuteNonQueryAsync("PRAGMA synchronous = NORMAL;");
            //await cmd.ExecuteNonQueryAsync("PRAGMA temp_store = MEMORY;");
            //await cmd.ExecuteNonQueryAsync("PRAGMA journal_mode = WAL;");
            //await cmd.ExecuteNonQueryAsync("PRAGMA cache_size = -512000;");
            //await cmd.ExecuteNonQueryAsync("PRAGMA threads = 8;");
            //await cmd.ExecuteNonQueryAsync("PRAGMA read_uncommitted = 1;");
            //await cmd.ExecuteNonQueryAsync("PRAGMA mmap_size = 41943040;");
            //await cmd.ExecuteNonQueryAsync("PRAGMA shared_cache = true;");
            var opts = Environment.GetEnvironmentVariable("CUSTOMSQLITEOPTIONS_DUPLICATI") ?? "";
            if (pagecachesize > MINIMUM_SQLITE_PAGE_CACHE_SIZE)
                opts = string.Format(CultureInfo.InvariantCulture, "cache_size=-{0};{1}", pagecachesize / 1024L, opts);

            if (string.IsNullOrWhiteSpace(opts))
                return con;

            using (var cmd = con.CreateCommand())
            using (var transaction = con.BeginTransaction(deferred: true))
            {
                cmd.Transaction = transaction;
                foreach (var opt in opts.Split([';'], StringSplitOptions.RemoveEmptyEntries))
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "CustomSQLiteOption", @"Setting custom SQLite option '{0}'.", opt);
                    try
                    {
                        cmd.CommandText = string.Format(CultureInfo.InvariantCulture, "PRAGMA {0}", opt);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "CustomSQLiteOption", ex, @"Error setting custom SQLite option '{0}'.", opt);
                    }
                }
                await transaction.CommitAsync();
            }
            return con;
        }

        public static Microsoft.Data.Sqlite.SqliteConnection LoadConnection(string targetpath, long pagecachesize)
        {
            return LoadConnectionAsync(targetpath, pagecachesize).Await();
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database
        /// </summary>
        /// <returns>The SQLite connection instance.</returns>
        /// <param name="targetpath">The optional path to the database.</param>
        /// <param name="pagecachesize"> The page cache size to set.</param>
        public static async Task<Microsoft.Data.Sqlite.SqliteConnection> LoadConnectionAsync(string targetpath, long pagecachesize)
        {
            if (string.IsNullOrWhiteSpace(targetpath))
                throw new ArgumentNullException(nameof(targetpath));

            var con = await LoadConnectionAsync();

            try
            {
                await OpenSQLiteFileAsync(con, targetpath);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "FailedToLoadConnectionSQLite", ex, @"Failed to load connection with path '{0}'.", targetpath);
                await DisposeConnectionAsync(con);

                throw;
            }

            // set custom Sqlite options
            return await ApplyCustomPragmasAsync(con, pagecachesize);
        }

        /// <summary>
        /// Returns the SQLiteCommand type for the current architecture
        /// </summary>
        public static Type SQLiteConnectionType
        {
            get
            {
                return typeof(Microsoft.Data.Sqlite.SqliteConnection);
            }
        }

        /// <summary>
        /// Returns the version string from the SQLite type
        /// </summary>
        public static string? SQLiteVersion
        {
            get
            {
                var versionString = SQLiteConnectionType.GetProperty("SQLiteVersion")?.GetValue(null, null) as string;
                if (string.IsNullOrWhiteSpace(versionString))
                {
                    // Support for Microsoft.Data.SQLite
                    // NOTE: Has an issue with ? as position parameters
                    var inst = Activator.CreateInstance(SQLiteConnectionType);
                    versionString = SQLiteConnectionType.GetProperty("ServerVersion")?.GetValue(inst, null) as string;
                }
                return versionString;
            }
        }

        /// <summary>
        /// Set environment variables to be used by SQLite to determine which folder to use for temporary files.
        /// From SQLite's documentation, SQLITE_TMPDIR is used for unix-like systems.
        /// For Windows, TMP and TEMP environment variables are used.
        /// </summary>
        private static void SetEnvironmentVariablesForSQLiteTempDir()
        {
            // Allow the user to override the temp folder for SQLite
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SQLITE_TMPDIR")))
                Environment.SetEnvironmentVariable("SQLITE_TMPDIR", Utility.TempFolder.SystemTempPath);
            Environment.SetEnvironmentVariable("TMPDIR", Utility.TempFolder.SystemTempPath);
            Environment.SetEnvironmentVariable("TMP", Utility.TempFolder.SystemTempPath);
            Environment.SetEnvironmentVariable("TEMP", Utility.TempFolder.SystemTempPath);
        }

        private static void DisposeConnection(Microsoft.Data.Sqlite.SqliteConnection? con)
        {
            DisposeConnectionAsync(con).Await();
        }

        /// <summary>
        /// Wrapper to dispose the SQLite connection
        /// </summary>
        /// <param name="con">The connection to close.</param>
        private static async Task DisposeConnectionAsync(Microsoft.Data.Sqlite.SqliteConnection? con)
        {
            if (con != null)
                try { await con.DisposeAsync(); }
                catch (Exception ex) { Logging.Log.WriteExplicitMessage(LOGTAG, "ConnectionDisposeError", ex, "Failed to dispose connection"); }
        }

        private static void OpenSQLiteFile(Microsoft.Data.Sqlite.SqliteConnection con, string path)
        {
            OpenSQLiteFileAsync(con, path).Await();
        }

        /// <summary>
        /// Opens the SQLite file in the given connection, creating the file if required
        /// </summary>
        /// <param name="con">The connection to use.</param>
        /// <param name="path">Path to the file to open, which may not exist.</param>
        private static async Task OpenSQLiteFileAsync(Microsoft.Data.Sqlite.SqliteConnection con, string path)
        {
            con.ConnectionString = $"Data Source={path};Pooling=false";
            await con.OpenAsync();
            // TODO legacy configuration?
            //if (con is Microsoft.Data.Sqlite.SqliteConnection sqlitecon && !OperatingSystem.IsMacOS())
            //{
            //    // These configuration options crash on MacOS (arm64), but the other platforms should be enough to detect incorrect SQL
            //    sqlitecon.SetConfigurationOption(System.Data.SQLite.SQLiteConfigDbOpsEnum.SQLITE_DBCONFIG_DQS_DDL, false);
            //    sqlitecon.SetConfigurationOption(System.Data.SQLite.SQLiteConfigDbOpsEnum.SQLITE_DBCONFIG_DQS_DML, false);
            //}

            // Make the file only accessible by the current user, unless opting out
            if (!SystemIO.IO_OS.FileExists(SystemIO.IO_OS.PathCombine(SystemIO.IO_OS.PathGetDirectoryName(path), Util.InsecurePermissionsMarkerFile)))
                try { SystemIO.IO_OS.FileSetPermissionUserRWOnly(path); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "SQLiteFilePermissionError", ex, "Failed to set permissions on SQLite file '{0}'", path); }
        }

        private static void TestSQLiteFile(Microsoft.Data.Sqlite.SqliteConnection con)
        {
            TestSQLiteFileAsync(con).Await();
        }

        /// <summary>
        /// Tests the SQLite connection, throwing an exception if the connection does not work
        /// </summary>
        /// <param name="con">The connection to test.</param>
        private static async Task TestSQLiteFileAsync(Microsoft.Data.Sqlite.SqliteConnection con)
        {
            // Do a dummy query to make sure we have a working db
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM SQLITE_MASTER";
            await cmd.ExecuteScalarAsync();
        }
    }
}
