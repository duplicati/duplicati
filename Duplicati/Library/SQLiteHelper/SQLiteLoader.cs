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
using System.Text;

namespace Duplicati.Library.SQLiteHelper
{
    public static class SQLiteLoader
    {
        /// <summary>
        /// A cached copy of the type
        /// </summary>
        private static Type m_type = null;

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
                    con.ConnectionString = "Data Source=" + targetpath;
                    con.Open();

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
                    var filename = "System.Data.SQLite.dll";
                    var basePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SQLite");

                    // Set this to make SQLite preload automatically
                    Environment.SetEnvironmentVariable("PreLoadSQLite_BaseDirectory", basePath);

                    //Default is to use the pinvoke version which requires a native .dll/.so
                    var assemblyPath = System.IO.Path.Combine(basePath, "pinvoke");

                    if (!Duplicati.Library.Utility.Utility.IsMono)
                    {
                        //If we run with MS.Net we can use the mixed mode assemblies
                        if (Library.Utility.Utility.Is64BitProcess)
                        {
                            if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basePath, "win64"), filename)))
                                assemblyPath = System.IO.Path.Combine(basePath, "win64");
                        }
                        else
                        {
                            if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basePath, "win32"), filename)))
                                assemblyPath = System.IO.Path.Combine(basePath, "win32");
                        }

                        // If we have a new path, try to force load the mixed-mode assembly for the current architecture
                        // This can be avoided if the preload in SQLite works, but it is easy to do it here as well
                        if (assemblyPath != System.IO.Path.Combine(basePath, "pinvoke"))
                        {
                            try { PInvoke.LoadLibraryEx(System.IO.Path.Combine(basePath, "SQLite.Interop.dll"), IntPtr.Zero, 0); }
                            catch { }
                        }

                    } else {
                        //On Mono, we try to find the Mono version of SQLite
                        
                        //This secret environment variable can be used to support older installations
                        var envvalue = System.Environment.GetEnvironmentVariable("DISABLE_MONO_DATA_SQLITE");
                        if (!Utility.Utility.ParseBool(envvalue, envvalue != null))
                        {
                            foreach(var asmversion in new string[] {"4.0.0.0", "2.0.0.0"})
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
                                    
                                } catch {
                                }
                            }

                            Console.WriteLine("Failed to load Mono.Data.Sqlite.SqliteConnection, reverting to built-in.");
                        }
                    }

                    m_type = System.Reflection.Assembly.LoadFile(System.IO.Path.Combine(assemblyPath, filename)).GetType("System.Data.SQLite.SQLiteConnection");
                }

                return m_type;
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
