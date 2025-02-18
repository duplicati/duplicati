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

namespace Duplicati.Library.Main
{

    public enum BackendActionType
    {
        List,
        Get,
        Put,
        Delete,
        CreateFolder,
        QuotaInfo,
        WaitForEmpty
    }

    public enum BackendEventType
    {
        Started,
        Progress,
        Completed,
        Retrying,
        Failed,
        Rename
    }

    /// <summary>
    /// The supported operations
    /// </summary>
    public enum OperationMode
    {
        Backup,
        Restore,
        List,
        ListAffected,
        ListChanges,
        Delete,
        RestoreControlfiles,
        Repair,
        CreateLogDb,
        Compact,
        Test,
        TestFilters,
        SystemInfo,
        ListRemote,
        ListBrokenFiles,
        PurgeBrokenFiles,
        PurgeFiles,
        SendMail,
        Vacuum,
    }

    /// <summary>
    /// Describes all states a remote volume can have
    /// </summary>
    public enum RemoteVolumeState
    {
        /// <summary>
        /// Indicates that the remote volume is being created
        /// </summary>
        Temporary,

        /// <summary>
        /// Indicates that the remote volume is being uploaded
        /// </summary>
        Uploading,

        /// <summary>
        /// Indicates that the remote volume has been uploaded
        /// </summary>
        Uploaded,

        /// <summary>
        /// Indicates that the remote volume has been uploaded,
        /// and seen by a list operation
        /// </summary>
        Verified,

        /// <summary>
        /// Indicates that the remote volume should be deleted
        /// </summary>
        Deleting,

        /// <summary>
        /// Indicates that the remote volume was successfully
        /// deleted from the remote location
        /// </summary>
        Deleted
    }

    /// <summary>
    /// Describes the known remote volume types
    /// </summary>
    public enum RemoteVolumeType
    {
        /// <summary>
        /// Contains data blocks
        /// </summary>
        Blocks,
        /// <summary>
        /// Contains file lists
        /// </summary>
        Files,
        /// <summary>
        /// Contains redundant lookup information
        /// </summary>
        Index
    }

    public enum FilelistEntryType
    {
        /// <summary>
        /// The actual type of the entry could not be determined
        /// </summary>
        Unknown,
        /// <summary>
        /// The entry is a plain file
        /// </summary>
        File,
        /// <summary>
        /// The entry is a folder
        /// </summary>
        Folder,
        /// <summary>
        /// The entry is an alternate data stream, or resource/data fork
        /// </summary>
        AlternateStream,
        /// <summary>
        /// The entry is a symbolic link
        /// </summary>
        Symlink
    }
}
