using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Implements a convenient wrapping for mapping a path to a drive letter
    /// </summary>
    public class DefineDosDevice : IDisposable
    {
        /// <summary>
        /// Encapsulation of Win32 calls
        /// </summary>
        private static class Win32API
        {
            /// <summary>
            /// Flags that can be used with DefineDosDevice
            /// </summary>
            [Flags]
            public enum DDD_Flags : uint
            {
                /// <summary>
                /// Uses the targetpath string as is. Otherwise, it is converted from an MS-DOS path to a path.
                /// </summary>
                DDD_RAW_TARGET_PATH = 0x1,
                /// <summary>
                /// Removes the specified definition for the specified device. To determine which definition to remove, the function walks the list of mappings for the device, looking for a match of targetpath against a prefix of each mapping associated with this device. The first mapping that matches is the one removed, and then the function returns.
                /// If targetpath is NULL or a pointer to a NULL string, the function will remove the first mapping associated with the device and pop the most recent one pushed. If there is nothing left to pop, the device name will be removed.
                /// If this value is not specified, the string pointed to by the targetpath parameter will become the new mapping for this device.
                /// </summary>
                DDD_REMOVE_DEFINITION = 0x2,
                /// <summary>
                /// If this value is specified along with DDD_REMOVE_DEFINITION, the function will use an exact match to determine which mapping to remove. Use this value to ensure that you do not delete something that you did not define.
                /// </summary>
                DDD_EXACT_MATCH_ON_REMOVE = 0x4,
                /// <summary>
                /// Do not broadcast the WM_SETTINGCHANGE message. By default, this message is broadcast to notify the shell and applications of the change.
                /// </summary>
                DDD_NO_BROADCAST_SYSTEM = 0x8
            }

            /// <summary>
            /// Defines, redefines, or deletes MS-DOS device names.
            /// </summary>
            /// <param name="flags">The controllable aspects of the DefineDosDevice function</param>
            /// <param name="devicename">A pointer to an MS-DOS device name string specifying the device the function is defining, redefining, or deleting. The device name string must not have a colon as the last character, unless a drive letter is being defined, redefined, or deleted. For example, drive C would be the string &quot;C:&quot;. In no case is a trailing backslash (&quot;\&quot;) allowed.</param>
            /// <param name="targetpath">A pointer to a path string that will implement this device. The string is an MS-DOS path string unless the DDD_RAW_TARGET_PATH flag is specified, in which case this string is a path string.</param>
            /// <returns>True on success, false otherwise</returns>
            [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError  = true)]
            public static extern bool DefineDosDevice(DDD_Flags flags, string devicename, string targetpath);
        }

        /// <summary>
        /// The drive the path is mapped to
        /// </summary>
        private string m_drive;
        /// <summary>
        /// The path that is mapped to the drive
        /// </summary>
        private string m_targetPath;
        /// <summary>
        /// A value indicating if the shell should be notified of changes
        /// </summary>
        private bool m_shellBroadcast;

        /// <summary>
        /// Gets the drive that this mapping represents
        /// </summary>
        public string Drive { get { return m_drive; } }

        /// <summary>
        /// Gets the path that this mapping represents
        /// </summary>
        public string Targetpath { get { return m_targetPath; } }

        /// <summary>
        /// Creates a new mapping, using default settings
        /// </summary>
        /// <param name="path">The path to map</param>
        public DefineDosDevice(string path)
            : this(path, null, false)
        {
        }

        /// <summary>
        /// Creates a new mapping
        /// </summary>
        /// <param name="path">The path to map</param>
        /// <param name="drive">The drive to map to, use null to get a free drive letter</param>
        /// <param name="notifyShell">True to notify the shell of the change, false otherwise</param>
        public DefineDosDevice(string path, string drive, bool notifyShell)
        {
            if (string.IsNullOrEmpty(drive))
            {
                List<char> drives = new List<char>("DEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());
                foreach (DriveInfo di in DriveInfo.GetDrives())
                {
                    if ((di.RootDirectory.FullName.Length == 2 && di.RootDirectory.FullName[1] == ':') || ((di.RootDirectory.FullName.Length == 3 && di.RootDirectory.FullName.EndsWith(":\\"))))
                    {
                        int i = drives.IndexOf(di.RootDirectory.FullName[0]);
                        if (i >= 0)
                            drives.RemoveAt(i);
                    }
                }

                if (drives.Count == 0)
                    throw new IOException("No drive letters available");
                drive = drives[0].ToString() + ':';
            }

            while (drive.EndsWith("\\"))
                drive = drive.Substring(0, drive.Length - 1);

            if (!drive.EndsWith(":"))
                throw new ArgumentException("The drive specification must end with a colon.", nameof(drive));

            Win32API.DDD_Flags flags = 0;
            if (!notifyShell)
                flags |= Win32API.DDD_Flags.DDD_NO_BROADCAST_SYSTEM;

            if (!Win32API.DefineDosDevice(flags, drive, path))
                throw new System.ComponentModel.Win32Exception();

            m_drive = drive;
            m_targetPath = path;
            m_shellBroadcast = notifyShell;
        }

        /// <summary>
        /// Disposes all resources held
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes all resources
        /// </summary>
        /// <param name="disposing">True if called from Disposing, false otherwise</param>
        protected void Dispose(bool disposing)
        {
            if (m_drive != null)
            {
                Win32API.DDD_Flags flags = Win32API.DDD_Flags.DDD_REMOVE_DEFINITION | Win32API.DDD_Flags.DDD_EXACT_MATCH_ON_REMOVE;
                if (m_shellBroadcast)
                    flags |= Win32API.DDD_Flags.DDD_NO_BROADCAST_SYSTEM;
                Win32API.DefineDosDevice(flags, m_drive, m_targetPath);
                m_drive = null;
            }

            if (disposing)
                GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Destroys the object and releases all held resources
        /// </summary>
        ~DefineDosDevice()
        {
            Dispose(false);
        }

    }
}
