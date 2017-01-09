using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main
{

    public enum BackendActionType
    {
        List,
        Get,
        Put,
        Delete,
        CreateFolder
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
        /// Indicattes that the remote volume should be deleted
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
