#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.GUI
{
    public static class SQLiteLoader
    {
        /// <summary>
        /// A cached copy of the type
        /// </summary>
        private static Type m_type = null;

        /// <summary>
        /// Returns the SQLiteCommand type for the current architecture
        /// </summary>
        public static Type SQLiteConnectionType
        {
            get
            {
                if (m_type == null)
                {
                    string filename = "System.Data.SQLite.dll";
                    string basePath = System.IO.Path.Combine(System.Windows.Forms.Application.StartupPath, "SQLite");

                    //Default is to use the pinvoke version which requires a native .dll/.so
                    string assemblyPath = System.IO.Path.Combine(basePath, "pinvoke");

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
                    } else {
						//On Mono, we try to find the Mono version of SQLite
						
						//This secrect commandline variable can be used to support older installations
						if (System.Environment.GetEnvironmentVariable("DISABLE_MONO_DATA_SQLITE") == null)
						{
							try 
							{
								Type t = System.Reflection.Assembly.Load("Mono.Data.Sqlite, Version=2.0.0.0, Culture=neutral, PublicKeyToken=0738eb9f132ed756").GetType("Mono.Data.Sqlite.SqliteConnection");
								if (t != null && t.GetInterface("System.Data.IDbConnection", false) != null)
								{
									Version v = new Version((string)t.GetProperty("SQLiteVersion").GetValue(null, null));
									if (v >= new Version(3, 6, 3))
									{
										m_type = t;
										return m_type;
									}
								}
								
							} catch (Exception ex){
								Console.WriteLine(string.Format("Failed to load Mono.Data.Sqlite.SqliteConnection, reverting to built-in.{0} Error message: {1}", Environment.NewLine, ex.ToString()));
							}
						}
					}

                    m_type = System.Reflection.Assembly.LoadFile(System.IO.Path.Combine(assemblyPath, filename)).GetType("System.Data.SQLite.SQLiteConnection");
                }

                return m_type;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.Winapi)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsWow64Process([System.Runtime.InteropServices.In] IntPtr hProcess, [System.Runtime.InteropServices.Out] out bool lpSystemInfo);

        private static bool Is32BitProcessOn64BitProcessor()
        {
            try
            {
                bool retVal;
                IsWow64Process(System.Diagnostics.Process.GetCurrentProcess().Handle, out retVal);
                return retVal;
            }
            catch
            {
                return false; //In case the OS is old enough not to have the Wow64 function
            }
        }
    }
}
