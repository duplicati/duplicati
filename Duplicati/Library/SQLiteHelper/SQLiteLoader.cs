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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SQLiteHelper
{
    public static class SQLiteLoader
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(SQLiteLoader));

        /// <summary>
        /// Helper method with logic to handle opening a database in possibly encrypted format
        /// </summary>
        /// <param name="con">The SQLite connection object</param>
        /// <param name="databasePath">The location of Duplicati's database.</param>
        /// <param name="decryptionPassword">The password to use for decryption.</param>
        public static void OpenDatabase(System.Data.IDbConnection con, string databasePath, string? decryptionPassword)
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
                OpenSQLiteFile(con, databasePath);
                TestSQLiteFile(con);
            }
            catch
            {
                try { con.Dispose(); }
                catch { }

                throw;
            }

            if (con.State != System.Data.ConnectionState.Open)
                throw new UserInformationException("Failed to open database for unknown reason, check the logs to see error messages", "DatabaseOpenFailed");
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database
        /// </summary>
        /// <returns>The SQLite connection instance.</returns>
        public static System.Data.IDbConnection LoadConnection()
        {
            System.Data.IDbConnection? con = null;
            SetEnvironmentVariablesForSQLiteTempDir();

            try
            {
                con = (System.Data.IDbConnection?)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "FailedToLoadConnectionSQLite", ex, "Failed to load connection.");
                DisposeConnection(con);

                throw;
            }

            return con ?? throw new InvalidOperationException("Failed to load connection");
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database
        /// </summary>
        /// <returns>The SQLite connection instance.</returns>
        /// <param name="targetpath">The optional path to the database.</param>
        public static System.Data.IDbConnection LoadConnection(string targetpath)
        {
            if (string.IsNullOrWhiteSpace(targetpath))
                throw new ArgumentNullException(nameof(targetpath));

            System.Data.IDbConnection con = LoadConnection();

            try
            {
                OpenSQLiteFile(con, targetpath);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "FailedToLoadConnectionSQLite", ex, @"Failed to load connection with path '{0}'.", targetpath);
                DisposeConnection(con);

                throw;
            }

            // set custom Sqlite options
            var opts = Environment.GetEnvironmentVariable("CUSTOMSQLITEOPTIONS_DUPLICATI");
            if (opts != null)
            {
                var topts = opts.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (topts.Length > 0)
                {
                    using (var cmd = con.CreateCommand())
                    {
                        foreach (var opt in topts)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "CustomSQLiteOption", @"Setting custom SQLite option '{0}'.", opt);
                            try
                            {
                                cmd.CommandText = string.Format("pragma {0}", opt);
                                cmd.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                Logging.Log.WriteErrorMessage(LOGTAG, "CustomSQLiteOption", ex, @"Error setting custom SQLite option '{0}'.", opt);
                            }
                        }
                    }
                }
            }

            return con;
        }

        /// <summary>
        /// Returns the SQLiteCommand type for the current architecture
        /// </summary>
        public static Type SQLiteConnectionType
        {
            get
            {
                return typeof(System.Data.SQLite.SQLiteConnection);
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
            System.Environment.SetEnvironmentVariable("SQLITE_TMPDIR", Library.Utility.TempFolder.SystemTempPath);
            System.Environment.SetEnvironmentVariable("TMP", Library.Utility.TempFolder.SystemTempPath);
            System.Environment.SetEnvironmentVariable("TEMP", Library.Utility.TempFolder.SystemTempPath);
        }

        /// <summary>
        /// Wrapper to dispose the SQLite connection
        /// </summary>
        /// <param name="con">The connection to close.</param>
        private static void DisposeConnection(System.Data.IDbConnection? con)
        {
            if (con != null)
                try { con.Dispose(); }
                catch (Exception ex) { Logging.Log.WriteExplicitMessage(LOGTAG, "ConnectionDisposeError", ex, "Failed to dispose connection"); }
        }

        /// <summary>
        /// Opens the SQLite file in the given connection, creating the file if required
        /// </summary>
        /// <param name="con">The connection to use.</param>
        /// <param name="path">Path to the file to open, which may not exist.</param>
        private static void OpenSQLiteFile(System.Data.IDbConnection con, string path)
        {
            // Check if SQLite database exists before opening a connection to it.
            // This information is used to 'fix' permissions on a newly created file.
            var fileExists = SystemIO.IO_OS.FileExists(path);

            con.ConnectionString = "Data Source=" + path;
            con.Open();
            if (con is System.Data.SQLite.SQLiteConnection sqlitecon && !OperatingSystem.IsMacOS())
            {
                // These configuration options crash on MacOS (arm64), but the other platforms should be enough to detect incorrect SQL
                sqlitecon.SetConfigurationOption(System.Data.SQLite.SQLiteConfigDbOpsEnum.SQLITE_DBCONFIG_DQS_DDL, false);
                sqlitecon.SetConfigurationOption(System.Data.SQLite.SQLiteConfigDbOpsEnum.SQLITE_DBCONFIG_DQS_DML, false);
            }

            // Make the file only accessible by the current user
            if (fileExists)
            {
                if (!SystemIO.IO_OS.FileExists(SystemIO.IO_OS.PathCombine(SystemIO.IO_OS.PathGetDirectoryName(path), Util.InsecurePermissionsMarkerFile)))
                    SystemIO.IO_OS.FileSetPermissionUserRWOnly(path);
            }
            else
            {
                SystemIO.IO_OS.FileSetPermissionUserRWOnly(path);
            }
        }

        /// <summary>
        /// Tests the SQLite connection, throwing an exception if the connection does not work
        /// </summary>
        /// <param name="con">The connection to test.</param>
        private static void TestSQLiteFile(System.Data.IDbConnection con)
        {
            // Do a dummy query to make sure we have a working db
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM SQLITE_MASTER";
                cmd.ExecuteScalar();
            }
        }
    }
}
