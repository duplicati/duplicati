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
using System.Security.AccessControl;


#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// This class contains various Windows system calls related to reading locked files.
    /// Some PInvoke signatures are from http://www.pinvoke.net
    /// </summary>
    public static class WinNativeMethods
    {
        [Flags]
        private enum FileAccess : uint
        {
            GenericNone = 0x00000000,
            GenericRead = 0x80000000,
            GenericWrite = 0x40000000,
            GenericExecute = 0x20000000,
            GenericAll = 0x10000000,
            ReadControl = 0x00020000
        }

        [Flags]
        private enum FileShare : uint
        {
            None = 0x00000000,
            Read = 0x00000001,
            Write = 0x00000002,
            ReadWrite = 0x00000003,
            Delete = 0x00000004
        }

        private enum CreationDisposition : uint
        {
            New = 1,
            CreateAlways = 2,
            OpenExisting = 3,
            OpenAlways = 4,
            TruncateExisting = 5
        }

        [Flags]
        private enum FileAttributes : uint
        {
            Readonly = 0x00000001,
            Hidden = 0x00000002,
            System = 0x00000004,
            Directory = 0x00000010,
            Archive = 0x00000020,
            Device = 0x00000040,
            Normal = 0x00000080,
            Temporary = 0x00000100,
            SparseFile = 0x00000200,
            ReparsePoint = 0x00000400,
            Compressed = 0x00000800,
            Offline = 0x00001000,
            NotContentIndexed = 0x00002000,
            Encrypted = 0x00004000,
            Write_Through = 0x80000000,
            Overlapped = 0x40000000,
            NoBuffering = 0x20000000,
            RandomAccess = 0x10000000,
            SequentialScan = 0x08000000,
            DeleteOnClose = 0x04000000,
            BackupSemantics = 0x02000000,
            PosixSemantics = 0x01000000,
            OpenReparsePoint = 0x00200000,
            OpenNoRecall = 0x00100000,
            FirstPipeInstance = 0x00080000
        }

        private const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
        private const int TOKEN_QUERY = 0x00000008;
        private const int SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_BACKUP_NAME = "SeBackupPrivilege";
        private const string SE_RESTORE_NAME = "SeRestorePrivilege";
        private const int TOKEN_INFORMATION_CLASS_PRIVILEGES  = 0x3;
        private const int ATTRIBUTE_FIXED_SIZE = 200;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public UInt32 LowPart;
            public Int32 HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public UInt32 Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public UInt32 PrivilegeCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = ATTRIBUTE_FIXED_SIZE)] //We make sure there is enough room
            public LUID_AND_ATTRIBUTES[] Privileges;
        }


        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int OpenProcessToken(int ProcessHandle, int DesiredAccess, ref int tokenhandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetCurrentProcess();

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetTokenInformation(int tokenhandle, int tokeninformationclass, [MarshalAs(UnmanagedType.Struct)]ref TOKEN_PRIVILEGES state, int bufferlength, ref int actualsize);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int LookupPrivilegeValue(string lpsystemname, string lpname, [MarshalAs(UnmanagedType.Struct)] ref LUID lpLuid);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int AdjustTokenPrivileges(int tokenhandle, bool disableprivs, [MarshalAs(UnmanagedType.Struct)]ref TOKEN_PRIVILEGES newState, int bufferlength, [MarshalAs(UnmanagedType.Struct)]ref TOKEN_PRIVILEGES previousState, ref int actualsize);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int LookupPrivilegeName(string lpSystemName, [MarshalAs(UnmanagedType.Struct)] ref LUID lpLuid, System.Text.StringBuilder lpName, ref int cchName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
           string lpFileName,
           FileAccess dwDesiredAccess,
           FileShare dwShareMode,
           IntPtr lpSecurityAttributes,
           CreationDisposition dwCreationDisposition,
           FileAttributes dwFlagsAndAttributes,
           IntPtr hTemplateFile
        );

        /// <summary>
        /// Opens a file with backup semantics, only works if the caller has the SeBackupPrivilege
        /// </summary>
        /// <param name="filename">The file to open</param>
        /// <returns>The opened backup filestream</returns>
        public static System.IO.Stream OpenAsBackupFile(string filename)
        {
            SafeFileHandle hFile = CreateFile(filename, FileAccess.ReadControl, FileShare.None, IntPtr.Zero, CreationDisposition.OpenExisting, FileAttributes.BackupSemantics | FileAttributes.SequentialScan, IntPtr.Zero);

            if (hFile.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            //TODO: We need to be able to seek to pos 0
            return new Alphaleonis.Win32.Filesystem.BackupFileStream(hFile, FileSystemRights.Read);
        }

        /// <summary>
        /// Internal helper function that returns two values relating to the SeBackupPrivilege
        /// </summary>
        /// <param name="hasPrivilege">True if the calling proccess has the SeBackupPrivilege, false otherwise</param>
        /// <param name="isEnabled">True if the SeBackupPrivilege is enabled, false otherwise</param>
        private static void GetBackupPrivilege(out bool hasPrivilege, out bool isEnabled)
        {
            int token = 0;
            int outsize = 0;

            TOKEN_PRIVILEGES TP = new TOKEN_PRIVILEGES();
            LUID backupLuid = new LUID();

            if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref token) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (LookupPrivilegeValue(null, SE_BACKUP_NAME, ref backupLuid) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (GetTokenInformation(token, TOKEN_INFORMATION_CLASS_PRIVILEGES, ref TP, Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), ref outsize) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

#if DEBUG
            Console.WriteLine("Read token information:");
            PrintTokenInformation(TP);
#endif

            for (int i = 0; i < TP.PrivilegeCount; i++)
                if (TP.Privileges[i].Luid.LowPart == backupLuid.LowPart && TP.Privileges[i].Luid.HighPart == backupLuid.HighPart)
                {
                    isEnabled = (TP.Privileges[i].Attributes & SE_PRIVILEGE_ENABLED) == SE_PRIVILEGE_ENABLED;
                    hasPrivilege = true;
                    return;
                }

            hasPrivilege = false;
            isEnabled = false;
        }

        /// <summary>
        /// Gets a value indicating if the current process can set the SeBackupPrivilege value
        /// </summary>
        public static bool CanEnableBackupPrivilege
        {
            get
            {
                bool hasPrivilege, isEnabled;
                GetBackupPrivilege(out hasPrivilege, out isEnabled);

                return hasPrivilege;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating if the current process has the backup privilege (SE_BACKUP_NAME)
        /// </summary>
        public static bool BackupPrivilege
        {
            get
            {
                bool hasPrivilege, isEnabled;
                GetBackupPrivilege(out hasPrivilege, out isEnabled);

                if (!hasPrivilege)
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.WinNativeMethod.MissingBackupPrivilegeError);

                return isEnabled;
            }
            set
            {
                if (!CanEnableBackupPrivilege)
                    throw new Duplicati.Library.Interface.UserInformationException(Strings.WinNativeMethod.MissingBackupPrivilegeError);

                int token = 0;
                int outsize = 0;

                TOKEN_PRIVILEGES TP = new TOKEN_PRIVILEGES();
                LUID backupLuid = new LUID();
                LUID restoreLuid = new LUID();

                if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref token) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (LookupPrivilegeValue(null, SE_BACKUP_NAME, ref backupLuid) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (LookupPrivilegeValue(null, SE_RESTORE_NAME, ref restoreLuid) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                TP.PrivilegeCount = 1;
                TP.Privileges = new LUID_AND_ATTRIBUTES[ATTRIBUTE_FIXED_SIZE];
                TP.Privileges[0].Attributes = value ? SE_PRIVILEGE_ENABLED : 0u;
                TP.Privileges[0].Luid = backupLuid;
                TP.Privileges[1].Attributes = value ? SE_PRIVILEGE_ENABLED : 0u;
                TP.Privileges[1].Luid = restoreLuid;

                TOKEN_PRIVILEGES TPOut = new TOKEN_PRIVILEGES();


                if (AdjustTokenPrivileges(token, false, ref TP, Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), ref TPOut, ref outsize) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

#if DEBUG
                Console.WriteLine("Modified token information:");
                PrintTokenInformation(TPOut);
#endif

                if (Marshal.GetLastWin32Error() != 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

#if DEBUG
        private static void PrintTokenInformation(TOKEN_PRIVILEGES tp)
        {
            int outsize;
            StringBuilder sbname = new StringBuilder(1024 * 4);

            for (int i = 0; i < tp.PrivilegeCount; i++)
            {
                LUID id = tp.Privileges[i].Luid;

                outsize = sbname.Capacity;
                if (LookupPrivilegeName(null, ref id, sbname, ref outsize) == 0)
                    Console.WriteLine("Invalid LUID {0}:{1}: {2}", id.HighPart, id.LowPart, new Win32Exception(Marshal.GetLastWin32Error()).Message);
                else
                    Console.WriteLine("{0}, status {1}", sbname, tp.Privileges[i].Attributes);
            }

        }
#endif

    }
}
