#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.IO;
using Duplicati.Library.Common;

namespace Duplicati.Library.SQLiteHelper
{
    public static class SQLiteLoader
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(SQLiteLoader));

        /// <summary>
        /// A cached copy of the type
        /// </summary>
        private static Type m_type = null;

        /// <summary>
        /// Helper method with logic to handle opening a database in possibly encrypted format
        /// </summary>
        /// <param name="con">The SQLite connection object</param>
        /// <param name="databasePath">The location of Duplicati's database.</param>
        /// <param name="useDatabaseEncryption">Specify if database is encrypted</param>
        /// <param name="password">Encryption password</param>
        public static void OpenDatabase(System.Data.IDbConnection con, string databasePath, bool useDatabaseEncryption, string password)
        {
            var setPwdMethod = con.GetType().GetMethod("SetPassword", new[] { typeof(string) });
            string attemptedPassword;

            if (!useDatabaseEncryption || string.IsNullOrEmpty(password))
                attemptedPassword = null; //No encryption specified, attempt to open without
            else
                attemptedPassword = password; //Encryption specified, attempt to open with

            if (setPwdMethod != null)
                setPwdMethod.Invoke(con, new object[] { attemptedPassword });

            try
            {
                //Attempt to open in preferred state
                OpenSQLiteFile(con, databasePath);
                TestSQLiteFile(con);
            }
            catch
            {
                try
                {
                    //We can't try anything else without a password
                    if (string.IsNullOrEmpty(password))
                        throw;

                    //Open failed, now try the reverse
                    attemptedPassword = attemptedPassword == null ? password : null;

                    con.Close();
                    if (setPwdMethod != null)
                        setPwdMethod.Invoke(con, new object[] { attemptedPassword });
                    OpenSQLiteFile(con, databasePath);

                    TestSQLiteFile(con);
                }
                catch
                {
                    try { con.Close(); }
                    catch (Exception ex) { Logging.Log.WriteExplicitMessage(LOGTAG, "OpenDatabaseFailed", ex, "Failed to open the SQLite database: {0}", databasePath); }
                }

                //If the db is not open now, it won't open
                if (con.State != System.Data.ConnectionState.Open)
                    throw; //Report original error

                //The open method succeeded with the non-default method, now change the password
                var changePwdMethod = con.GetType().GetMethod("ChangePassword", new[] { typeof(string) });
                changePwdMethod.Invoke(con, new object[] { useDatabaseEncryption ? password : null });
            }
        }

        /// <summary>
        /// Loads an SQLite connection instance and opening the database
        /// </summary>
        /// <returns>The SQLite connection instance.</returns>
        public static System.Data.IDbConnection LoadConnection()
        {
            System.Data.IDbConnection con = null;
            SetEnvironmentVariablesForSQLiteTempDir();

            try
            {
                con = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "FailedToLoadConnectionSQLite", ex, "Failed to load connection.");
                DisposeConnection(con);

                throw;
            }

            return con;
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
            return con;
        }

        /// <summary>
        /// Returns the SQLiteCommand type for the current architecture
        /// </summary>
        public static Type SQLiteConnectionType
        {
            get
            {
                if (m_type != null)
                    return m_type;

                var filename = "System.Data.SQLite.dll";
                var basePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SQLite");

                // Set this to make SQLite preload automatically
                Environment.SetEnvironmentVariable("PreLoadSQLite_BaseDirectory", basePath);

                //Default is to use the pinvoke version which requires a native .dll/.so
                var assemblyPath = Path.Combine(basePath, "pinvoke");
                var loadMixedModeAssembly = false;

                if (!Duplicati.Library.Utility.Utility.IsMono)
                {
                    //If we run with MS.Net we can use the mixed mode assemblies
                    if (Environment.Is64BitProcess)
                    {
                        if (File.Exists(Path.Combine(Path.Combine(basePath, "win64"), filename)))
                        {
                            assemblyPath = Path.Combine(basePath, "win64");
                            loadMixedModeAssembly = true;
                        }
                    }
                    else
                    {
                        if (File.Exists(Path.Combine(Path.Combine(basePath, "win32"), filename)))
                        {
                            assemblyPath = Path.Combine(basePath, "win32");
                            loadMixedModeAssembly = true;
                        }
                    }

                    // If we have a new path, try to force load the mixed-mode assembly for the current architecture
                    // This can be avoided if the preload in SQLite works, but it is easy to do it here as well
                    if (loadMixedModeAssembly)
                    {
                        try { PInvoke.LoadLibraryEx(Path.Combine(assemblyPath, "SQLite.Interop.dll"), IntPtr.Zero, 0); }
                        catch (Exception ex) { Logging.Log.WriteExplicitMessage(LOGTAG, "LoadMixedModeSQLiteError", ex, "Failed to load the mixed mode SQLite database: {0}", Path.Combine(assemblyPath, "SQLite.Interop.dll")); }
                    }
                }
                else
                {
                    //On Mono, we try to find the Mono version of SQLite

                    //This secret environment variable can be used to support older installations
                    var envvalue = System.Environment.GetEnvironmentVariable("DISABLE_MONO_DATA_SQLITE");
                    if (!Utility.Utility.ParseBool(envvalue, envvalue != null))
                    {
                        foreach (var asmversion in new[] { "4.0.0.0", "2.0.0.0" })
                        {
                            var name = string.Format("Mono.Data.Sqlite, Version={0}, Culture=neutral, PublicKeyToken=0738eb9f132ed756", asmversion);
                            try
                            {
                                Type t = System.Reflection.Assembly.Load(name).GetType("Mono.Data.Sqlite.SqliteConnection");
                                if (t != null && t.GetInterface("System.Data.IDbConnection", false) != null)
                                {
                                    Version v = new Version((string)t.GetProperty("SQLiteVersion").GetValue(null, null));
                                    if (v >= new Version(3, 6, 3))
                                    {
                                        return m_type = t;
                                    }
                                }
                            }
                            catch(Exception ex)
                            {
                                Logging.Log.WriteExplicitMessage(LOGTAG, "FailedToLoadSQLiteAssembly", ex, "Failed to load the SQLite assembly: {0}", name);
                            }
                        }

                        Logging.Log.WriteVerboseMessage(LOGTAG, "FailedToLoadSQLite", "Failed to load Mono.Data.Sqlite.SqliteConnection, reverting to built-in.");
                    }
                }

                m_type = System.Reflection.Assembly.LoadFile(Path.Combine(assemblyPath, filename)).GetType("System.Data.SQLite.SQLiteConnection");

                return m_type;
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
        private static void DisposeConnection(System.Data.IDbConnection con)
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
            var fileExists = false;
            if (!Platform.IsClientWindows)
                fileExists = File.Exists(path);

            con.ConnectionString = "Data Source=" + path + ";journal mode=Memory";
            con.Open();

            // Enable write-ahead logging
            using (System.Data.IDbCommand command = con.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode = WAL;PRAGMA mmap_size=268435456;";
                command.ExecuteNonQuery();
            }

            // If we are non-Windows, make the file only accessible by the current user
            if (!Platform.IsClientWindows && !fileExists)
                SetUnixPermissionUserRWOnly(path);
        }

        /// <summary>
        /// Sets the unix permission user read-write Only.
        /// </summary>
        /// <param name="path">The file to set permissions on.</param>
        /// <remarks> Make sure we do not inline this, as we might eventually load Mono.Posix, which is not present on Windows</remarks>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void SetUnixPermissionUserRWOnly(string path)
        {
            var fi = UnixSupport.File.GetUserGroupAndPermissions(path);
            UnixSupport.File.SetUserGroupAndPermissions(
                    path, 
                    fi.UID, 
                    fi.GID, 
                    0x180 /* FilePermissions.S_IRUSR | FilePermissions.S_IWUSR*/
                );
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

    /// <summary>
    /// Helper class with PInvoke methods
    /// </summary>
    internal static class PInvoke
    {
        /// <summary>
        /// Loads the specified module into the address space of the calling process.
        /// </summary>
        /// <returns>The library ex.</returns>
        /// <param name="lpFileName">The filename of the module to load.</param>
        /// <param name="hReservedNull">Reserved for future use.</param>
        /// <param name="dwFlags">Action to take on load.</param>
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);
    }
}
