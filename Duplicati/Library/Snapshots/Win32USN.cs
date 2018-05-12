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
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Duplicati.Library.Snapshots
{
    //The signatures in this file are from http://pinvoke.net

    /// <summary>
    /// Various Windows specific calls to support USN
    /// </summary>
    internal static class Win32USN
    {
        public const int ERROR_HANDLE_EOF = 38;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_SUCCESS = 0;
        public const int ERROR_JOURNAL_ENTRY_DELETED = 1181;

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct USN_JOURNAL_DATA_V0
        {
            /// <summary>
            /// The current journal identifier. A journal is assigned a new identifier on creation and can be 
            /// stamped with a new identifier in the course of its existence. 
            /// The NTFS file system uses this identifier for an integrity check.
            /// </summary>
            public long UsnJournalID;

            /// <summary>
            /// The number of first record that can be read from the journal.
            /// </summary>
            public long FirstUsn;

            /// <summary>
            /// The number of next record to be written to the journal.
            /// </summary>
            public long NextUsn;

            /// <summary>
            /// The first record that was written into the journal for this journal instance. 
            /// Enumerating the files or directories on a volume can return a USN lower than this value 
            /// (in other words, a FirstUsn member value less than the LowestValidUsn member value). 
            /// If it does, the journal has been stamped with a new identifier since the last USN was written. 
            /// In this case, LowestValidUsn may indicate a discontinuity in the journal, in which changes 
            /// to some or all files or directories on the volume may have occurred that are not recorded in the change journal.
            /// </summary>
            public long LowestValidUsn;

            /// <summary>
            /// The largest USN that the change journal supports. An administrator must delete 
            /// the change journal as the value of NextUsn approaches this value.
            /// </summary>
            public long MaxUsn;

            /// <summary>
            /// The target maximum size for the change journal, in bytes. The change journal can grow larger 
            /// than this value, but it is then truncated at the next NTFS file system checkpoint to less than this value.
            /// </summary>
            public long MaximumSize;

            /// <summary>
            /// The number of bytes of disk memory added to the end and removed from the beginning of the change 
            /// journal each time memory is allocated or deallocated. In other words, allocation and deallocation
            /// take place in units of this size. An integer multiple of a cluster size is a reasonable value for this member.
            /// </summary>
            public long AllocationDelta;    //DWORDLONG AllocationDelta
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct READ_USN_JOURNAL_DATA_V0
        {
            public long StartUsn;
            public USNReason ReasonMask;
            public uint ReturnOnlyOnClose;
            public ulong Timeout;
            public ulong BytesToWaitFor;
            public long UsnJournalID;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct USN_RECORD_V2
        {
            public uint RecordLength;
            public ushort MajorVersion;
            public ushort MinorVersion;
            public ulong FileReferenceNumber;
            public ulong ParentFileReferenceNumber;
            public long Usn;
            public long TimeStamp;  // strictly, this is a LARGE_INTEGER in C
            public USNReason Reason;
            public uint SourceInfo;
            public uint SecurityId;
            public FileAttributes FileAttributes;
            public ushort FileNameLength;
            public ushort FileNameOffset;
            // immediately after the FileNameOffset comes an array of WCHARs containing the FileName
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MFT_ENUM_DATA
        {
            public ulong StartFileReferenceNumber;
            public long LowUsn;
            public long HighUsn;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }
        #endregion

        #region Enums
        [Flags]
        public enum EMethod : uint
        {
            Buffered = 0,
            InDirect = 1,
            OutDirect = 2,
            Neither = 3
        }

        [Flags]
        public enum FsCtl : uint
        {
            /// <summary>
            /// Causes a journal to be queried when used with DeviceIoControl
            /// </summary>
            /// <remarks>FSCTL_QUERY_USN_JOURNAL</remarks>
            QueryUSNJournal = 0x000900f4,

            /// <summary>
            /// Causes a journal to be read when used with DeviceIoControl
            /// </summary>
            /// <remarks>FSCTL_READ_USN_JOURNAL</remarks>
            ReadUSNJournal = 0x000900bb,

            /// <summary>
            /// Enumerates the update sequence number (USN) data between two specified boundaries to obtain master file table (MFT) records.
            /// </summary>
            /// <remarks>FSCTL_ENUM_USN_DATA</remarks>
            EnumUSNData = 0x000900b3
        }

        [Flags]
        public enum FileAccess : uint
        {
            /// <summary>
            /// 
            /// </summary>
            GenericRead = 0x80000000,
            /// <summary>
            /// 
            /// </summary>
            GenericWrite = 0x40000000,
            /// <summary>
            /// 
            /// </summary>
            GenericExecute = 0x20000000,
            /// <summary>
            /// 
            /// </summary>
            GenericAll = 0x10000000
        }

        [Flags]
        public enum FileShare : uint
        {
            /// <summary>
            /// 
            /// </summary>
            None = 0x00000000,
            /// <summary>
            /// Enables subsequent open operations on an object to request read access. 
            /// Otherwise, other processes cannot open the object if they request read access. 
            /// If this flag is not specified, but the object has been opened for read access, the function fails.
            /// </summary>
            Read = 0x00000001,
            /// <summary>
            /// Enables subsequent open operations on an object to request write access. 
            /// Otherwise, other processes cannot open the object if they request write access. 
            /// If this flag is not specified, but the object has been opened for write access, the function fails.
            /// </summary>
            Write = 0x00000002,
            /// <summary>
            /// Enables subsequent open operations on an object to request delete access. 
            /// Otherwise, other processes cannot open the object if they request delete access.
            /// If this flag is not specified, but the object has been opened for delete access, the function fails.
            /// </summary>
            Delete = 0x00000004,
            /// <summary>
            /// Combination of read and write
            /// </summary>
            ReadWrite = Read | Write,
            /// <summary>
            /// Combo flag that specifies all access
            /// </summary>
            All = None | Read | Write
        }

        public enum CreationDisposition : uint
        {
            /// <summary>
            /// Creates a new file. The function fails if a specified file exists.
            /// </summary>
            New = 1,
            /// <summary>
            /// Creates a new file, always. 
            /// If a file exists, the function overwrites the file, clears the existing attributes, combines the specified file attributes, 
            /// and flags with FILE_ATTRIBUTE_ARCHIVE, but does not set the security descriptor that the SECURITY_ATTRIBUTES structure specifies.
            /// </summary>
            CreateAlways = 2,
            /// <summary>
            /// Opens a file. The function fails if the file does not exist. 
            /// </summary>
            OpenExisting = 3,
            /// <summary>
            /// Opens a file, always. 
            /// If a file does not exist, the function creates a file as if dwCreationDisposition is CREATE_NEW.
            /// </summary>
            OpenAlways = 4,
            /// <summary>
            /// Opens a file and truncates it so that its size is 0 (zero) bytes. The function fails if the file does not exist.
            /// The calling process must open the file with the GENERIC_WRITE access right. 
            /// </summary>
            TruncateExisting = 5
        }

        [Flags]
        public enum FileAttributes : uint
        {
            None = 0x0,
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
            WriteThrough = 0x80000000,
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

        [Flags]
        public enum USNReason : uint
        {
            USN_REASON_DATA_OVERWRITE = 0x00000001,
            USN_REASON_DATA_EXTEND = 0x00000002,
            USN_REASON_DATA_TRUNCATION = 0x00000004,
            USN_REASON_NAMED_DATA_OVERWRITE = 0x00000010,
            USN_REASON_NAMED_DATA_EXTEND = 0x00000020,
            USN_REASON_NAMED_DATA_TRUNCATION = 0x00000040,
            USN_REASON_FILE_CREATE = 0x00000100,
            USN_REASON_FILE_DELETE = 0x00000200,
            USN_REASON_EA_CHANGE = 0x00000400,
            USN_REASON_SECURITY_CHANGE = 0x00000800,
            USN_REASON_RENAME_OLD_NAME = 0x00001000,
            USN_REASON_RENAME_NEW_NAME = 0x00002000,
            USN_REASON_INDEXABLE_CHANGE = 0x00004000,
            USN_REASON_BASIC_INFO_CHANGE = 0x00008000,
            USN_REASON_HARD_LINK_CHANGE = 0x00010000,
            USN_REASON_COMPRESSION_CHANGE = 0x00020000,
            USN_REASON_ENCRYPTION_CHANGE = 0x00040000,
            USN_REASON_OBJECT_ID_CHANGE = 0x00080000,
            USN_REASON_REPARSE_POINT_CHANGE = 0x00100000,
            USN_REASON_STREAM_CHANGE = 0x00200000,
            USN_REASON_CLOSE = 0x80000000,
            USN_REASON_ANY =
                USN_REASON_DATA_OVERWRITE |
                USN_REASON_DATA_EXTEND |
                USN_REASON_DATA_TRUNCATION |
                USN_REASON_NAMED_DATA_OVERWRITE |
                USN_REASON_NAMED_DATA_EXTEND |
                USN_REASON_NAMED_DATA_TRUNCATION |
                USN_REASON_FILE_CREATE |
                USN_REASON_FILE_DELETE |
                USN_REASON_EA_CHANGE |
                USN_REASON_SECURITY_CHANGE |
                USN_REASON_RENAME_OLD_NAME |
                USN_REASON_RENAME_NEW_NAME |
                USN_REASON_INDEXABLE_CHANGE |
                USN_REASON_BASIC_INFO_CHANGE |
                USN_REASON_HARD_LINK_CHANGE |
                USN_REASON_COMPRESSION_CHANGE |
                USN_REASON_ENCRYPTION_CHANGE |
                USN_REASON_OBJECT_ID_CHANGE |
                USN_REASON_REPARSE_POINT_CHANGE |
                USN_REASON_STREAM_CHANGE |
                USN_REASON_CLOSE 
        }

        #endregion


        #region Function calls



        /// <summary>
        /// The CreateFile function creates or opens a file, file stream, directory, physical disk, volume, console buffer, tape drive,
        /// communications resource, mailslot, or named pipe. The function returns a handle that can be used to access an object.
        /// </summary>
        /// <param name="lpFileName"></param>
        /// <param name="dwDesiredAccess"> access to the object, which can be read, write, or both</param>
        /// <param name="dwShareMode">The sharing mode of an object, which can be read, write, both, or none</param>
        /// <param name="SecurityAttributes">A pointer to a SECURITY_ATTRIBUTES structure that determines whether or not the returned handle can 
        /// be inherited by child processes. Can be null</param>
        /// <param name="dwCreationDisposition">An action to take on files that exist and do not exist</param>
        /// <param name="dwFlagsAndAttributes">The file attributes and flags. </param>
        /// <param name="hTemplateFile">A handle to a template file with the GENERIC_READ access right. The template file supplies file attributes 
        /// and extended attributes for the file that is being created. This parameter can be null</param>
        /// <returns>If the function succeeds, the return value is an open handle to a specified file. If a specified file exists before the function 
        /// all and dwCreationDisposition is CREATE_ALWAYS or OPEN_ALWAYS, a call to GetLastError returns ERROR_ALREADY_EXISTS, even when the function 
        /// succeeds. If a file does not exist before the call, GetLastError returns 0 (zero).
        /// If the function fails, the return value is INVALID_HANDLE_VALUE. To get extended error information, call GetLastError.
        /// </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
              string lpFileName,
              FileAccess dwDesiredAccess,
              FileShare dwShareMode,
              IntPtr SecurityAttributes,
              CreationDisposition dwCreationDisposition,
              FileAttributes dwFlagsAndAttributes,
              IntPtr hTemplateFile
              );

       /// <summary>
        /// Sends the dwIoControlCode to the device specified by hDevice.
        /// </summary>
        /// <param name="hDevice">Safe handle to the device </param>
        /// <param name="IoControlCode">Device IO Control Code to send</param>
        /// <param name="InBuffer">Input buffer if required</param>
        /// <param name="nInBufferSize">Size of input buffer</param>
        /// <param name="OutBuffer">Output buffer if required</param>
        /// <param name="nOutBufferSize">Size of output buffer</param>
        /// <param name="pBytesReturned">Number of bytes returned in output buffer</param>
        /// <param name="overlapped">IntPtr to an 'OVERLAPPED' structure</param>
        /// <returns>'true' if successful, otherwise 'false'</returns>
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            FsCtl IoControlCode,
            [In] READ_USN_JOURNAL_DATA_V0 InBuffer,
            uint nInBufferSize,
            [In] IntPtr OutBuffer,
            uint nOutBufferSize,
            ref uint pBytesReturned,
            [In] IntPtr overlapped //[In] ref System.Threading.NativeOverlapped Overlapped
        );

        /// <summary>
        /// Sends the dwIoControlCode to the device specified by hDevice.
        /// </summary>
        /// <param name="hDevice">Safe handle to the device </param>
        /// <param name="IoControlCode">Device IO Control Code to send</param>
        /// <param name="InBuffer">Input buffer if required</param>
        /// <param name="nInBufferSize">Size of input buffer</param>
        /// <param name="OutBuffer">Output buffer if required</param>
        /// <param name="nOutBufferSize">Size of output buffer</param>
        /// <param name="pBytesReturned">Number of bytes returned in output buffer</param>
        /// <param name="overlapped">IntPtr to an 'OVERLAPPED' structure</param>
        /// <returns>'true' if successful, otherwise 'false'</returns>
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            FsCtl IoControlCode,
            [In] ref MFT_ENUM_DATA InBuffer,
            uint nInBufferSize,
            [In] IntPtr OutBuffer,
            uint nOutBufferSize,
            ref uint pBytesReturned,
            [In] IntPtr overlapped //[In] ref System.Threading.NativeOverlapped Overlapped
        );

        /// <summary>
        /// Sends the dwIoControlCode to the device specified by hDevice.
        /// </summary>
        /// <param name="hDevice">Safe handle to the device </param>
        /// <param name="dwIoControlCode">Device IO Control Code to send</param>
        /// <param name="lpInBuffer">Input buffer if required</param>
        /// <param name="nInBufferSize">Size of input buffer</param>
        /// <param name="lpOutBuffer">Output buffer if required</param>
        /// <param name="nOutBufferSize">Size of output buffer</param>
        /// <param name="pBytesReturned">Number of bytes returned in output buffer</param>
        /// <param name="overlapped">IntPtr to an 'OVERLAPPED' structure</param>
        /// <returns>'true' if successful, otherwise 'false'</returns>
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint pBytesReturned,
            IntPtr overlapped
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandle(
           [In] SafeFileHandle hFile,
           [Out] out BY_HANDLE_FILE_INFORMATION lpFileInformation
        );

        #endregion

        /// <summary>
        /// Sends the control code to the device specified by handle.
        /// </summary>
        /// <typeparam name="TStructure"></typeparam>
        /// <param name="handle"></param>
        /// <param name="code"></param>
        /// <param name="structure"></param>
        /// <param name="bufferSize">Maximum size of returned buffer</param>
        /// <returns></returns>
        public static bool ControlWithInput<TStructure>(
            SafeFileHandle handle, FsCtl code,
            ref TStructure structure, int bufferSize, out byte[] buffer)
            where TStructure : struct
        {
            uint datalen;
            bool controlResult;

            buffer = new byte[bufferSize];
            var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            var structureHandle = GCHandle.Alloc(structure, GCHandleType.Pinned);
            var bufferPointer = bufferHandle.AddrOfPinnedObject();
            var structurePointer = structureHandle.AddrOfPinnedObject();

            try
            {
                controlResult =
                    DeviceIoControl(handle, (uint)code,
                    structurePointer, (uint)Marshal.SizeOf(structure),
                    bufferPointer, (uint)buffer.Length,
                    out datalen, IntPtr.Zero);
            }
            finally
            {
                structureHandle.Free();
                bufferHandle.Free();
            }

            Array.Resize(ref buffer, (int)datalen);

            return controlResult;
        }

        /// <summary>
        /// Sends the control code to the device specified by handle.
        /// </summary>
        /// <typeparam name="TStructure"></typeparam>
        /// <param name="handle"></param>
        /// <param name="code"></param>
        /// <param name="structure"></param>
        internal static bool ControlWithOutput<TStructure>(
            SafeFileHandle handle, FsCtl code, ref TStructure structure)
            where TStructure : struct
        {
            bool controlResult;
            //get our object pointer
            var structureHandle = GCHandle.Alloc(structure, GCHandleType.Pinned);
            var structurePointer = structureHandle.AddrOfPinnedObject();

            try
            {
                controlResult =
                    DeviceIoControl(handle, (uint)code,
                    IntPtr.Zero, 0, structurePointer,
                    (uint)Marshal.SizeOf(structure),
                    out var _, IntPtr.Zero);
            }
            finally
            {
                // always release GH handle
                structureHandle.Free();
            }

            if (controlResult)
            {
                structure = (TStructure)Marshal.PtrToStructure(structurePointer, typeof(TStructure));
            }

            return controlResult;

        }
    }
}
