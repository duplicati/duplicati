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
                    string assemblyPath = System.IO.Path.Combine(basePath, "pinvoke");

                    if (System.Environment.OSVersion.Platform == PlatformID.Win32NT || System.Environment.OSVersion.Platform == PlatformID.Win32Windows)
                    {
                        if (IntPtr.Size == 8 || (IntPtr.Size == 4 && Is32BitProcessOn64BitProcessor()))
                        {
                            if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basePath, "win64"), filename)))
                                assemblyPath = System.IO.Path.Combine(basePath, "win64");
                        }
                        else
                        {
                            if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basePath, "win32"), filename)))
                                assemblyPath = System.IO.Path.Combine(basePath, "win32");
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
