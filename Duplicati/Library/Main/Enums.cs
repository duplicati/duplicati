// Copyright (C) 2026, The Duplicati Team
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
        WaitForEmpty,
        SetObjectLock,
        GetObjectLock
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
        Sync,
        ListFilesets,
        ListFolder,
        ListFileVersions,
        SearchFiles,
        SetLock,
        ReadLockInfo,
        RemoteSynchronization
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

    /// <summary>
    /// Describes the operations tracked by the sync <c>PendingOperation</c> journal.
    /// Each value corresponds to an in-flight intent recorded before the operation
    /// is performed on the remote destination, so a crash can be reconciled on resume.
    /// </summary>
    public enum SyncOperation
    {
        /// <summary>
        /// The file is being uploaded to the remote destination for the first time.
        /// </summary>
        Upload,

        /// <summary>
        /// The file is being updated on the remote destination (overwriting an existing entry).
        /// </summary>
        Update,

        /// <summary>
        /// The file is being deleted from the remote destination.
        /// </summary>
        Delete
    }

    /// <summary>
    /// Controls how the sync operation determines the remote state of each folder
    /// it processes. The choice trades off remote listing calls against local
    /// database freshness, and bounds how much work is done per folder.
    /// </summary>
    public enum SyncRemoteState
    {
        /// <summary>
        /// Always enumerate the remote target folder to obtain its current contents.
        /// The local database is not used as a diff baseline, so the database never
        /// holds a full inventory. This is the safest option: the remote destination
        /// is authoritative for each folder, at the cost of one listing per folder.
        /// </summary>
        UseRemoteState,

        /// <summary>
        /// Use the local database as the diff baseline and assume it is up to date,
        /// saving the remote listing calls. The inventory table is queried per
        /// folder instead of listing the remote destination. This is faster but can
        /// miss changes made to the remote destination outside of Duplicati; use
        /// <see cref="UseRemoteState"/> (or a recheck) when the remote may have
        /// drifted.
        /// </summary>
        UseLocalState,

        /// <summary>
        /// Do not enumerate the remote destination and do not consult the local
        /// database: upload every local file unconditionally, recreating folders as
        /// needed. This is the fastest path for an initial upload to an empty or
        /// disposable destination. Deleting remote files is not meaningful under
        /// this mode (there is no remote state to diff against); a request to delete
        /// is logged as a warning and skipped.
        /// </summary>
        BlindlyUpload
    }

    /// <summary>
    /// Controls how the <c>--restore-all-files</c> option behaves during a restore.
    /// </summary>
    public enum RestoreAllFilesMode
    {
        /// <summary>
        /// The option is not active; restore proceeds as a normal single-version
        /// restore (the most recent matching version, or the version selected by
        /// <c>--version</c>/<c>--time</c>).
        /// </summary>
        False = 0,

        /// <summary>
        /// Restore every targeted version into its own timestamp-named subfolder
        /// below the restore target. Every (non-filtered) file is restored in
        /// every version; no cross-version de-duplication is performed.
        /// </summary>
        True = 1,

        /// <summary>
        /// Like <see cref="True"/>, but subsequent versions skip any file whose
        /// content (file hash) was already restored in a previous version. The
        /// uniqueness check is performed on the file hash only, not on metadata.
        /// </summary>
        Unique = 2
    }
}
