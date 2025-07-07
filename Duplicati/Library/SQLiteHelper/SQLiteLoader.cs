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
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SQLiteHelper
{
    /// <summary>
    /// Provides methods to load and manage SQLite connections, including handling encrypted databases.
    /// </summary>
    public static class SQLiteLoader
    {
        /// <summary>
        /// The tag used for logging.
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(SQLiteLoader));

        /// <summary>
        /// Helper method with logic to handle opening a database in possibly encrypted format.
        /// </summary>
        /// <param name="con">The SQLite connection object.</param>
        /// <param name="databasePath">The location of Duplicati's database.</param>
        /// <param name="decryptionPassword">The password to use for decryption.</param>
        /// <returns>A task that completes when the database is opened.</returns>
        /// <exception cref="UserInformationException">Thrown if the database cannot be opened or decrypted.</exception>
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
                await OpenSQLiteFileAsync(con, databasePath)
                    .ConfigureAwait(false);
                await TestSQLiteFileAsync(con).ConfigureAwait(false);
            }
            catch
            {
                try { await con.DisposeAsync().ConfigureAwait(false); }
                catch { }

                throw;
            }

            if (con.State != System.Data.ConnectionState.Open)
                throw new UserInformationException("Failed to open database for unknown reason, check the logs to see error messages", "DatabaseOpenFailed");
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database.
        /// </summary>
        /// <returns>The SQLite connection instance.</returns>
        /// <remarks>
        /// This method is synchronous and should be used when you need to load the connection immediately. It calls the asynchronous version and waits for it to complete.
        /// </remarks>
        public static Microsoft.Data.Sqlite.SqliteConnection LoadConnection()
        {
            return LoadConnectionAsync().Await();
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database.
        /// </summary>
        /// <returns>A task that when awaited returns the SQLite connection instance.</returns>
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
                await DisposeConnectionAsync(con).ConfigureAwait(false);

                throw;
            }

            return con ?? throw new InvalidOperationException("Failed to load connection");
        }

        /// <summary>
        /// Applies user-supplied custom pragmas to the SQLite connection.
        /// </summary>
        /// <param name="con">The connection to apply the pragmas to.</param>
        /// <returns>A task that when awaited returns the connection with the pragmas applied.</returns>
        public static async Task<Microsoft.Data.Sqlite.SqliteConnection> ApplyCustomPragmasAsync(Microsoft.Data.Sqlite.SqliteConnection con)
        {
            Dictionary<string, string> customOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "synchronous", "NORMAL" }, // NORMAL is more performant than FULL (the default), but less safe, as it no longer blocks until the disk sync syscall to the OS has completed.
                { "temp_store", "MEMORY" }, // Use memory for temporary storage to improve performance.
                { "journal_mode", "WAL" }, // Use Write-Ahead Logging for better concurrency and performance.
                { "cache_size", "-65536" }, // Set cache size to 64 MB (negative value means in KB, so -64000 = 64 MB). Default is 2000 pages, which is 2 MB (2000 * 1024 bytes).
                { "mmap_size", "67108864" }, // 64 MB.
                { "threads", "8" }, // Use 8 threads for parallel processing where applicable.
                { "shared_cache", "true" } //
            };
            // Override the default options with any custom options set in the environment variable.
            var customOptionsEnv = Environment.GetEnvironmentVariable("CUSTOMSQLITEOPTIONS");
            if (!string.IsNullOrWhiteSpace(customOptionsEnv))
            {
                foreach (var opt in customOptionsEnv.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = opt.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                        customOptions[parts[0].Trim()] = parts[1].Trim();
                }
            }

            using (var cmd = con.CreateCommand())
            {
                foreach (var (key, value) in customOptions)
                {
                    var opt = $"{key}={value}";
                    Logging.Log.WriteVerboseMessage(LOGTAG, "CustomSQLiteOption", @"Setting custom SQLite option '{0}'.", opt);
                    try
                    {
                        cmd.CommandText = string.Format(CultureInfo.InvariantCulture, "PRAGMA {0}", opt);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "CustomSQLiteOption", ex, @"Error setting custom SQLite option '{0}'.", opt);
                    }
                }
            }
            return con;
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database with a specified page cache size.
        /// </summary>
        /// <param name="targetpath">The optional path to the database.</param>
        /// <returns>The SQLite connection instance.</returns>
        /// <remarks>
        /// This method is synchronous and should be used when you need to load the connection immediately. It calls the asynchronous version and waits for it to complete.
        /// </remarks>
        public static Microsoft.Data.Sqlite.SqliteConnection LoadConnection(string targetpath)
        {
            return LoadConnectionAsync(targetpath).Await();
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database.
        /// </summary>
        /// <param name="targetpath">The optional path to the database.</param>
        /// <returns>A task that when waited returns the SQLite connection instance.</returns>
        public static async Task<Microsoft.Data.Sqlite.SqliteConnection> LoadConnectionAsync(string targetpath)
        {
            if (string.IsNullOrWhiteSpace(targetpath))
                throw new ArgumentNullException(nameof(targetpath));

            var con = await LoadConnectionAsync().ConfigureAwait(false);

            try
            {
                await OpenSQLiteFileAsync(con, targetpath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "FailedToLoadConnectionSQLite", ex, @"Failed to load connection with path '{0}'.", targetpath);
                await DisposeConnectionAsync(con).ConfigureAwait(false);

                throw;
            }

            // set custom Sqlite options
            return await ApplyCustomPragmasAsync(con)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the SQLiteCommand type for the current architecture.
        /// </summary>
        public static Type SQLiteConnectionType
        {
            get
            {
                return typeof(Microsoft.Data.Sqlite.SqliteConnection);
            }
        }

        /// <summary>
        /// Returns the version string from the SQLite type.
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

        /// <summary>
        /// Wrapper to dispose the SQLite connection.
        /// </summary>
        /// <param name="con">The connection to close.</param>
        /// <returns>A task that completes when the connection is disposed.</returns>
        private static async Task DisposeConnectionAsync(Microsoft.Data.Sqlite.SqliteConnection? con)
        {
            if (con != null)
                try { await con.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { Logging.Log.WriteExplicitMessage(LOGTAG, "ConnectionDisposeError", ex, "Failed to dispose connection"); }
        }

        /// <summary>
        /// Opens the SQLite file in the given connection, creating the file if required.
        /// </summary>
        /// <param name="con">The connection to use.</param>
        /// <param name="path">Path to the file to open, which may not exist.</param>
        /// <returns>A task that completes when the file is opened.</returns>
        private static async Task OpenSQLiteFileAsync(Microsoft.Data.Sqlite.SqliteConnection con, string path)
        {
            con.ConnectionString = $"Data Source={path};Pooling=false";
            await con.OpenAsync().ConfigureAwait(false);

            // Make the file only accessible by the current user, unless opting out
            if (!SystemIO.IO_OS.FileExists(SystemIO.IO_OS.PathCombine(SystemIO.IO_OS.PathGetDirectoryName(path), Util.InsecurePermissionsMarkerFile)))
                try { SystemIO.IO_OS.FileSetPermissionUserRWOnly(path); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "SQLiteFilePermissionError", ex, "Failed to set permissions on SQLite file '{0}'", path); }
        }

        /// <summary>
        /// Tests the SQLite connection, throwing an exception if the connection does not work.
        /// </summary>
        /// <param name="con">The connection to test.</param>
        /// <returns>A task that completes when the test query is executed.</returns>
        private static async Task TestSQLiteFileAsync(Microsoft.Data.Sqlite.SqliteConnection con)
        {
            // Do a dummy query to make sure we have a working db
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM SQLITE_MASTER";
            await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        }
    }
}
