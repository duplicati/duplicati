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
using System.Collections.Generic;
using System.IO;
using System.Text;

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


        private const string SQLiteAssembly = "System.Data.SQLite.dll";

        /// <summary>
        /// Helper method with logic to handle opening a database in possibly encrypted format
        /// </summary>
        /// <param name="con">The SQLite connection object</param>
        /// <param name="DatabasePath">The location of Duplicati's database.</param>
        /// <param name="useDatabaseEncryption">Specify if database is encrypted</param>
        /// <param name="password">Encryption password</param>
        public static void OpenDatabase(System.Data.IDbConnection con, string DatabasePath, bool useDatabaseEncryption, string password)
        {
            System.Reflection.MethodInfo setPwdMethod = con.GetType().GetMethod("SetPassword", new[] { typeof(string) });
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
                OpenSQLiteFile(con, DatabasePath);

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
                    OpenSQLiteFile(con, DatabasePath);

                    TestSQLiteFile(con);
                }
                catch
                {
                    try { con.Close(); }
                    catch { }
                }

                //If the db is not open now, it won't open
                if (con.State != System.Data.ConnectionState.Open)
                    throw; //Report original error

                //The open method succeeded with the non-default method, now change the password
                System.Reflection.MethodInfo changePwdMethod = con.GetType().GetMethod("ChangePassword", new[] { typeof(string) });
                changePwdMethod.Invoke(con, new object[] { useDatabaseEncryption ? password : null });
            }
        }

        /// <summary>
        /// Loads an SQLite connection instance, optionally setting the tempfolder and opening the the database
        /// </summary>
        /// <returns>The SQLite connection instance.</returns>
        /// <param name="targetpath">The optional path to the database.</param>
        /// <param name="tempdir">The optional tempdir to set.</param>
        public static System.Data.IDbConnection LoadConnection(string targetpath = null, string tempdir = null)
        {
            if (string.IsNullOrWhiteSpace(tempdir))
                tempdir = Library.Utility.TempFolder.SystemTempPath;

            var prev = System.Environment.GetEnvironmentVariable("SQLITE_TMPDIR");

            System.Data.IDbConnection con = null;

            try
            {
                System.Environment.SetEnvironmentVariable("SQLITE_TMPDIR", tempdir);
                con = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType);
                if (!string.IsNullOrWhiteSpace(targetpath))
                {
                    OpenSQLiteFile(con, targetpath);

                    // Try to set the temp_dir even tough it is deprecated
                    if (!string.IsNullOrWhiteSpace(tempdir))
                    {
                        try
                        {
                            using (var cmd = con.CreateCommand())
                            {
                                cmd.CommandText = string.Format("PRAGMA temp_store_directory = '{0}'", tempdir);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch
                        {
                        }
                    }
                }

            }
            catch
            {
                if (con != null)
                    try { con.Dispose(); }
                    catch { }

                throw;
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("SQLITE_TMPDIR", prev);
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
                if (m_type == null)
                {
                    var basePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SQLite");

                    // Set this to make SQLite preload automatically
                    Environment.SetEnvironmentVariable("PreLoadSQLite_BaseDirectory", basePath);

                    //Default is to use the pinvoke version which requires a native .dll/.so
                    var assemblyPath = System.IO.Path.Combine(basePath, "pinvoke");

                    if (!Duplicati.Library.Utility.Utility.IsMono)
                    {
                        //If we run with MS.Net we can use the mixed mode assemblies
                        if (Environment.Is64BitProcess)
                        {
                            if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basePath, "win64"), SQLiteAssembly)))
                                assemblyPath = System.IO.Path.Combine(basePath, "win64");
                        }
                        else
                        {
                            if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basePath, "win32"), SQLiteAssembly)))
                                assemblyPath = System.IO.Path.Combine(basePath, "win32");
                        }

                        // If we have a new path, try to force load the mixed-mode assembly for the current architecture
                        // This can be avoided if the preload in SQLite works, but it is easy to do it here as well
                        if (assemblyPath != System.IO.Path.Combine(basePath, "pinvoke"))
                        {
                            try { PInvoke.LoadLibraryEx(System.IO.Path.Combine(basePath, "SQLite.Interop.dll"), IntPtr.Zero, 0); }
                            catch { }
                        }

                    }
                    else
                    {
                        //On Mono, we try to find the Mono version of SQLite

                        //This secret environment variable can be used to support older installations
                        var envvalue = System.Environment.GetEnvironmentVariable("DISABLE_MONO_DATA_SQLITE");
                        if (!Utility.Utility.ParseBool(envvalue, envvalue != null))
                        {
                            foreach (var asmversion in new string[] { "4.0.0.0", "2.0.0.0" })
                            {
                                try
                                {
                                    Type t = System.Reflection.Assembly.Load(string.Format("Mono.Data.Sqlite, Version={0}, Culture=neutral, PublicKeyToken=0738eb9f132ed756", asmversion)).GetType("Mono.Data.Sqlite.SqliteConnection");
                                    if (t != null && t.GetInterface("System.Data.IDbConnection", false) != null)
                                    {
                                        Version v = new Version((string)t.GetProperty("SQLiteVersion").GetValue(null, null));
                                        if (v >= new Version(3, 6, 3))
                                        {
                                            m_type = t;
                                            return m_type;
                                        }
                                    }

                                }
                                catch
                                {
                                }
                            }

                            Logging.Log.WriteVerboseMessage(LOGTAG, "FailedToLoadSQLite", "Failed to load Mono.Data.Sqlite.SqliteConnection, reverting to built-in.");
                        }
                    }

                    m_type = System.Reflection.Assembly.LoadFile(System.IO.Path.Combine(assemblyPath, SQLiteAssembly)).GetType("System.Data.SQLite.SQLiteConnection");
                }

                return m_type;
            }
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
            if (!Library.Utility.Utility.IsClientWindows)
                fileExists = File.Exists(path);

            con.ConnectionString = "Data Source=" + path;
            con.Open();

            // If we are non-Windows, make the file only accessible by the current user
            if (!Library.Utility.Utility.IsClientWindows && !fileExists)
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
