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

#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.Library.WindowsModules.Vss;

/// <summary>
/// HRESULT status codes returned by <see cref="IVssAsync.QueryStatus"/>
/// </summary>
internal enum VssAsyncStatus
{
    /// <summary>The asynchronous operation is still running</summary>
    VSS_S_ASYNC_PENDING = 0x00042309,
    /// <summary>The asynchronous operation has completed successfully</summary>
    VSS_S_ASYNC_FINISHED = 0x0004230A,
    /// <summary>The asynchronous operation has been cancelled</summary>
    VSS_S_ASYNC_CANCELLED = 0x0004230B,
}

/// <summary>
/// The context of a shadow copy operation (VSS_SNAPSHOT_CONTEXT)
/// </summary>
internal enum VssSnapshotContext : int
{
    /// <summary>Standard backup context (VSS_CTX_BACKUP)</summary>
    VSS_CTX_BACKUP = 0,
    /// <summary>File share backup context (VSS_CTX_FILE_SHARE_BACKUP)</summary>
    VSS_CTX_FILE_SHARE_BACKUP = 0x00000010,
    /// <summary>NAS rollback context (VSS_CTX_NAS_ROLLBACK)</summary>
    VSS_CTX_NAS_ROLLBACK = 0x00000019,
    /// <summary>App rollback context (VSS_CTX_APP_ROLLBACK)</summary>
    VSS_CTX_APP_ROLLBACK = 0x00000009,
    /// <summary>Client accessible context (VSS_CTX_CLIENT_ACCESSIBLE)</summary>
    VSS_CTX_CLIENT_ACCESSIBLE = 0x0000001D,
    /// <summary>Client accessible with writers context (VSS_CTX_CLIENT_ACCESSIBLE_WRITERS)</summary>
    VSS_CTX_CLIENT_ACCESSIBLE_WRITERS = 0x0000000D,
    /// <summary>All contexts (VSS_CTX_ALL)</summary>
    VSS_CTX_ALL = unchecked((int)0xFFFFFFFF),
}

/// <summary>
/// The type of backup operation (VSS_BACKUP_TYPE)
/// </summary>
internal enum VssBackupType : int
{
    /// <summary>Undefined backup type</summary>
    VSS_BT_UNDEFINED = 0,
    /// <summary>Full backup: all files, regardless of whether they have been backed up before</summary>
    VSS_BT_FULL = 1,
    /// <summary>Incremental backup: files changed since the last backup</summary>
    VSS_BT_INCREMENTAL = 2,
    /// <summary>Differential backup: files changed since the last full backup</summary>
    VSS_BT_DIFFERENTIAL = 3,
    /// <summary>Log backup</summary>
    VSS_BT_LOG = 4,
    /// <summary>Copy backup: all files, without resetting the backup archive bit</summary>
    VSS_BT_COPY = 5,
    /// <summary>Other backup type</summary>
    VSS_BT_OTHER = 6,
}

/// <summary>
/// The type of a VSS object (VSS_OBJECT_TYPE)
/// </summary>
internal enum VssObjectType : int
{
    /// <summary>Undefined object type</summary>
    VSS_OBJECT_UNKNOWN = 0,
    /// <summary>No object</summary>
    VSS_OBJECT_NONE = 1,
    /// <summary>A snapshot set</summary>
    VSS_OBJECT_SNAPSHOT_SET = 2,
    /// <summary>A snapshot</summary>
    VSS_OBJECT_SNAPSHOT = 3,
    /// <summary>A provider</summary>
    VSS_OBJECT_PROVIDER = 4,
    /// <summary>Count of object types</summary>
    VSS_OBJECT_TYPE_COUNT = 5,
}

/// <summary>
/// The usage type reported by a writer (VSS_USAGE_TYPE)
/// </summary>
internal enum VssUsageType : int
{
    /// <summary>Undefined usage</summary>
    VSS_UT_UNDEFINED = 0,
    /// <summary>Bootable system state</summary>
    VSS_UT_BOOTABLESYSTEMSTATE = 1,
    /// <summary>System service</summary>
    VSS_UT_SYSTEMSERVICE = 2,
    /// <summary>User data</summary>
    VSS_UT_USERDATA = 3,
    /// <summary>Other</summary>
    VSS_UT_OTHER = 4,
}

/// <summary>
/// The source type reported by a writer (VSS_SOURCE_TYPE)
/// </summary>
internal enum VssSourceType : int
{
    /// <summary>Undefined source</summary>
    VSS_ST_UNDEFINED = 0,
    /// <summary>Transacted database</summary>
    VSS_ST_TRANSACTEDDB = 1,
    /// <summary>Non-transacted database</summary>
    VSS_ST_NONTRANSACTEDDB = 2,
    /// <summary>Other</summary>
    VSS_ST_OTHER = 3,
}

/// <summary>
/// The type of a component (VSS_COMPONENT_TYPE)
/// </summary>
internal enum VssComponentType : int
{
    /// <summary>Undefined component type</summary>
    VSS_CT_UNDEFINED = 0,
    /// <summary>Database component</summary>
    VSS_CT_DATABASE = 1,
    /// <summary>File group component</summary>
    VSS_CT_FILEGROUP = 2,
}

/// <summary>
/// The state of a snapshot (VSS_SNAPSHOT_STATE)
/// </summary>
internal enum VssSnapshotState : int
{
    /// <summary>Unknown state</summary>
    VSS_SS_UNKNOWN = 0,
    /// <summary>Preparing</summary>
    VSS_SS_PREPARING = 1,
    /// <summary>Processing prepare</summary>
    VSS_SS_PROCESSING_PREPARE = 2,
    /// <summary>Prepared</summary>
    VSS_SS_PREPARED = 3,
    /// <summary>Processing pre-commit</summary>
    VSS_SS_PROCESSING_PRECOMMIT = 4,
    /// <summary>Pre-committed</summary>
    VSS_SS_PRECOMMITTED = 5,
    /// <summary>Processing commit</summary>
    VSS_SS_PROCESSING_COMMIT = 6,
    /// <summary>Committed</summary>
    VSS_SS_COMMITTED = 7,
    /// <summary>Processing post-commit</summary>
    VSS_SS_PROCESSING_POSTCOMMIT = 8,
    /// <summary>Processing pre-final commit</summary>
    VSS_SS_PROCESSING_PREFINALCOMMIT = 9,
    /// <summary>Pre-final committed</summary>
    VSS_SS_PREFINALCOMMITTED = 10,
    /// <summary>Processing post-final commit</summary>
    VSS_SS_PROCESSING_POSTFINALCOMMIT = 11,
    /// <summary>Created</summary>
    VSS_SS_CREATED = 12,
    /// <summary>Aborted</summary>
    VSS_SS_ABORTED = 13,
    /// <summary>Deleted</summary>
    VSS_SS_DELETED = 14,
    /// <summary>Post-committed</summary>
    VSS_SS_POSTCOMMITTED = 15,
    /// <summary>Count of states</summary>
    VSS_SS_COUNT = 16,
}

/// <summary>
/// Properties of a shadow copy (VSS_SNAPSHOT_PROP).
/// The string pointers are owned by VSS and must be released
/// by calling <see cref="NativeMethods.VssFreeSnapshotProperties"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VSS_SNAPSHOT_PROP
{
    /// <summary>The snapshot id</summary>
    public Guid m_SnapshotId;
    /// <summary>The snapshot set id</summary>
    public Guid m_SnapshotSetId;
    /// <summary>Number of volumes in the snapshot</summary>
    public int m_lSnapshotsCount;
    /// <summary>The device object path of the snapshot</summary>
    public IntPtr m_pwszSnapshotDeviceObject;
    /// <summary>The original volume name</summary>
    public IntPtr m_pwszOriginalVolumeName;
    /// <summary>The machine that created the snapshot</summary>
    public IntPtr m_pwszOriginatingMachine;
    /// <summary>The machine running the VSS service</summary>
    public IntPtr m_pwszServiceMachine;
    /// <summary>The exposed name of the snapshot</summary>
    public IntPtr m_pwszExposedName;
    /// <summary>The exposed path of the snapshot</summary>
    public IntPtr m_pwszExposedPath;
    /// <summary>The provider id</summary>
    public Guid m_ProviderId;
    /// <summary>The snapshot attributes</summary>
    public int m_lSnapshotAttributes;
    /// <summary>The creation timestamp (in FILETIME units)</summary>
    public long m_tsCreationTimestamp;
    /// <summary>The snapshot state</summary>
    public VssSnapshotState m_eStatus;
}

/// <summary>
/// Component information returned by <see cref="IVssWMComponent.GetComponentInfo"/>
/// (VSS_COMPONENTINFO). Must be freed with <see cref="IVssWMComponent.FreeComponentInfo"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VSS_COMPONENTINFO
{
    /// <summary>The component type</summary>
    public VssComponentType type;
    /// <summary>The logical path of the component</summary>
    public IntPtr bstrLogicalPath;
    /// <summary>The name of the component</summary>
    public IntPtr bstrComponentName;
    /// <summary>The caption of the component</summary>
    public IntPtr bstrCaption;
    /// <summary>Pointer to the icon data</summary>
    public IntPtr pbIcon;
    /// <summary>Size of the icon data</summary>
    public uint cbIcon;
    /// <summary>Whether restore metadata is available</summary>
    public byte bRestoreMetadata;
    /// <summary>Whether the writer wants a notification when the backup completes</summary>
    public byte bNotifyOnBackupComplete;
    /// <summary>Whether the component is selectable</summary>
    public byte bSelectable;
    /// <summary>Whether the component is selectable for restore</summary>
    public byte bSelectableForRestore;
    /// <summary>Component flags (VSS_CF_*)</summary>
    public uint dwComponentFlags;
    /// <summary>Number of files in the component</summary>
    public uint cFileCount;
    /// <summary>Number of databases in the component</summary>
    public uint cDatabases;
    /// <summary>Number of log files in the component</summary>
    public uint cLogFiles;
    /// <summary>Number of dependencies in the component</summary>
    public uint cDependencies;
}

/// <summary>
/// The IVssAsync interface is a COM interface that is used to control
/// asynchronous VSS operations.
/// </summary>
[ComImport]
[Guid("507C37B4-CF5B-4e95-B0AF-14EB9767467E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVssAsync
{
    /// <summary>
    /// Cancels an incomplete asynchronous operation
    /// </summary>
    [PreserveSig]
    int Cancel();

    /// <summary>
    /// Waits until an asynchronous operation completes and returns the result
    /// </summary>
    /// <param name="dwMilliseconds">The maximum number of milliseconds to wait</param>
    [PreserveSig]
    int Wait(uint dwMilliseconds);

    /// <summary>
    /// Queries the status of an asynchronous operation
    /// </summary>
    /// <param name="pHrResult">The status of the asynchronous operation</param>
    /// <param name="pReserved">Reserved, must be null</param>
    [PreserveSig]
    int QueryStatus(out int pHrResult, IntPtr pReserved);
}

/// <summary>
/// The IVssBackupComponents interface is a COM interface that defines
/// the methods for creating and managing shadow copies.
/// </summary>
[ComImport]
[Guid("665c1d5f-c218-414d-a05d-7fef5f9d5c86")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVssBackupComponents
{
    /// <summary>
    /// Returns the number of writer components
    /// </summary>
    [PreserveSig]
    int GetWriterComponentsCount(out uint pcComponents);

    /// <summary>
    /// Returns the specified writer components
    /// </summary>
    [PreserveSig]
    int GetWriterComponents(uint iWriter, out IntPtr ppWriter);

    /// <summary>
    /// Initializes the backup components for a backup operation
    /// </summary>
    [PreserveSig]
    int InitializeForBackup([MarshalAs(UnmanagedType.BStr)] string? bstrXML);

    /// <summary>
    /// Sets the state of the backup operation
    /// </summary>
    [PreserveSig]
    int SetBackupState(
        [MarshalAs(UnmanagedType.U1)] bool bSelectComponents,
        [MarshalAs(UnmanagedType.U1)] bool bBackupBootableSystemState,
        VssBackupType backupType,
        [MarshalAs(UnmanagedType.U1)] bool bPartialFileSupport);

    /// <summary>
    /// Initializes the backup components for a restore operation
    /// </summary>
    [PreserveSig]
    int InitializeForRestore([MarshalAs(UnmanagedType.BStr)] string bstrXML);

    /// <summary>
    /// Sets the state of the restore operation
    /// </summary>
    [PreserveSig]
    int SetRestoreState(int restoreType);

    /// <summary>
    /// Gathers the writer metadata
    /// </summary>
    [PreserveSig]
    int GatherWriterMetadata(out IVssAsync pAsync);

    /// <summary>
    /// Returns the number of writers with metadata
    /// </summary>
    [PreserveSig]
    int GetWriterMetadataCount(out uint pcWriters);

    /// <summary>
    /// Returns the metadata of the specified writer
    /// </summary>
    [PreserveSig]
    int GetWriterMetadata(uint iWriter, out Guid pidInstance, out IVssExamineWriterMetadata ppMetadata);

    /// <summary>
    /// Releases the writer metadata
    /// </summary>
    [PreserveSig]
    int FreeWriterMetadata();

    /// <summary>
    /// Adds a component to the backup set
    /// </summary>
    [PreserveSig]
    int AddComponent(
        Guid instanceId,
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName);

    /// <summary>
    /// Prepares the backup operation
    /// </summary>
    [PreserveSig]
    int PrepareForBackup(out IVssAsync ppAsync);

    /// <summary>
    /// Aborts the backup operation
    /// </summary>
    [PreserveSig]
    int AbortBackup();

    /// <summary>
    /// Gathers the writer status
    /// </summary>
    [PreserveSig]
    int GatherWriterStatus(out IVssAsync pAsync);

    /// <summary>
    /// Returns the number of writers with status
    /// </summary>
    [PreserveSig]
    int GetWriterStatusCount(out uint pcWriters);

    /// <summary>
    /// Releases the writer status
    /// </summary>
    [PreserveSig]
    int FreeWriterStatus();

    /// <summary>
    /// Returns the status of the specified writer
    /// </summary>
    [PreserveSig]
    int GetWriterStatus(
        uint iWriter,
        out Guid pidInstance,
        out Guid pidWriter,
        [MarshalAs(UnmanagedType.BStr)] out string pbstrWriter,
        out int pnStatus,
        out int phResultFailure);

    /// <summary>
    /// Sets the backup succeeded state for a component
    /// </summary>
    [PreserveSig]
    int SetBackupSucceeded(
        Guid instanceId,
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.U1)] bool bSucceded);

    /// <summary>
    /// Sets the backup options for a component
    /// </summary>
    [PreserveSig]
    int SetBackupOptions(
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.LPWStr)] string wszBackupOptions);

    /// <summary>
    /// Sets the selected-for-restore state for a component
    /// </summary>
    [PreserveSig]
    int SetSelectedForRestore(
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.U1)] bool bSelectedForRestore);

    /// <summary>
    /// Sets the restore options for a component
    /// </summary>
    [PreserveSig]
    int SetRestoreOptions(
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.LPWStr)] string wszRestoreOptions);

    /// <summary>
    /// Sets the additional restores state for a component
    /// </summary>
    [PreserveSig]
    int SetAdditionalRestores(
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.U1)] bool bAdditionalRestores);

    /// <summary>
    /// Sets the previous backup stamp for a component
    /// </summary>
    [PreserveSig]
    int SetPreviousBackupStamp(
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.LPWStr)] string wszPreviousBackupStamp);

    /// <summary>
    /// Saves the backup components as XML
    /// </summary>
    [PreserveSig]
    int SaveAsXML([MarshalAs(UnmanagedType.BStr)] ref string pbstrXML);

    /// <summary>
    /// Signals that the backup operation has completed
    /// </summary>
    [PreserveSig]
    int BackupComplete(out IVssAsync ppAsync);

    /// <summary>
    /// Adds an alternate location mapping
    /// </summary>
    [PreserveSig]
    int AddAlternativeLocationMapping(
        Guid writerId,
        VssComponentType componentType,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.LPWStr)] string wszPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszFilespec,
        [MarshalAs(UnmanagedType.U1)] bool bRecursive,
        [MarshalAs(UnmanagedType.LPWStr)] string wszDestination);

    /// <summary>
    /// Adds a restore subcomponent
    /// </summary>
    [PreserveSig]
    int AddRestoreSubcomponent(
        Guid writerId,
        VssComponentType componentType,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.LPWStr)] string wszSubComponentLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszSubComponentName,
        [MarshalAs(UnmanagedType.U1)] bool bRepair);

    /// <summary>
    /// Sets the file restore status for a component
    /// </summary>
    [PreserveSig]
    int SetFileRestoreStatus(
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        int status);

    /// <summary>
    /// Adds a new target
    /// </summary>
    [PreserveSig]
    int AddNewTarget(
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        [MarshalAs(UnmanagedType.LPWStr)] string wszPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszFileName,
        [MarshalAs(UnmanagedType.U1)] bool bRecursive,
        [MarshalAs(UnmanagedType.LPWStr)] string wszAlternatePath);

    /// <summary>
    /// Sets the ranges file path
    /// </summary>
    [PreserveSig]
    int SetRangesFilePath(
        Guid writerId,
        VssComponentType ct,
        [MarshalAs(UnmanagedType.LPWStr)] string wszLogicalPath,
        [MarshalAs(UnmanagedType.LPWStr)] string wszComponentName,
        uint iPartialFile,
        [MarshalAs(UnmanagedType.LPWStr)] string wszRangesFile);

    /// <summary>
    /// Prepares the restore operation
    /// </summary>
    [PreserveSig]
    int PreRestore(out IVssAsync ppAsync);

    /// <summary>
    /// Finalizes the restore operation
    /// </summary>
    [PreserveSig]
    int PostRestore(out IVssAsync ppAsync);

    /// <summary>
    /// Sets the context for the shadow copy operation
    /// </summary>
    [PreserveSig]
    int SetContext(int lContext);

    /// <summary>
    /// Starts a new snapshot set
    /// </summary>
    [PreserveSig]
    int StartSnapshotSet(out Guid pSnapshotSetId);

    /// <summary>
    /// Adds a volume to the snapshot set
    /// </summary>
    [PreserveSig]
    int AddToSnapshotSet(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszVolumeName,
        Guid ProviderId,
        out Guid pidSnapshot);

    /// <summary>
    /// Commits the snapshot set
    /// </summary>
    [PreserveSig]
    int DoSnapshotSet(out IVssAsync ppAsync);

    /// <summary>
    /// Deletes snapshots
    /// </summary>
    [PreserveSig]
    int DeleteSnapshots(
        Guid SourceObjectId,
        VssObjectType eSourceObjectType,
        [MarshalAs(UnmanagedType.Bool)] bool bForceDelete,
        out int plDeletedSnapshots,
        out Guid pNondeletedSnapshotID);

    /// <summary>
    /// Imports snapshots
    /// </summary>
    [PreserveSig]
    int ImportSnapshots(out IVssAsync ppAsync);

    /// <summary>
    /// Breaks a snapshot set
    /// </summary>
    [PreserveSig]
    int BreakSnapshotSet(Guid SnapshotSetId);

    /// <summary>
    /// Gets the properties of a snapshot
    /// </summary>
    [PreserveSig]
    int GetSnapshotProperties(Guid SnapshotId, out VSS_SNAPSHOT_PROP pProp);

    /// <summary>
    /// Queries VSS objects
    /// </summary>
    [PreserveSig]
    int Query(Guid QueriedObjectId, VssObjectType eQueriedObjectType, VssObjectType eReturnedObjectsType, IntPtr ppEnum);

    /// <summary>
    /// Checks if a volume is supported for shadow copies
    /// </summary>
    [PreserveSig]
    int IsVolumeSupported(
        Guid ProviderId,
        [MarshalAs(UnmanagedType.LPWStr)] string pwszVolumeName,
        [MarshalAs(UnmanagedType.Bool)] out bool pbSupportedByThisProvider);

    /// <summary>
    /// Disables the specified writer classes
    /// </summary>
    [PreserveSig]
    int DisableWriterClasses(
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] Guid[] rgWriterClassId,
        uint cClassId);

    /// <summary>
    /// Enables the specified writer classes
    /// </summary>
    [PreserveSig]
    int EnableWriterClasses(
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] Guid[] rgWriterClassId,
        uint cClassId);

    /// <summary>
    /// Disables the specified writer instances
    /// </summary>
    [PreserveSig]
    int DisableWriterInstances(
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] Guid[] rgWriterInstanceId,
        uint cInstanceId);

    /// <summary>
    /// Exposes a snapshot
    /// </summary>
    [PreserveSig]
    int ExposeSnapshot(
        Guid SnapshotId,
        [MarshalAs(UnmanagedType.LPWStr)] string wszPathFromRoot,
        int lAttributes,
        [MarshalAs(UnmanagedType.LPWStr)] string wszExpose,
        [MarshalAs(UnmanagedType.LPWStr)] out string pwszExposed);

    /// <summary>
    /// Reverts to a snapshot
    /// </summary>
    [PreserveSig]
    int RevertToSnapshot(Guid SnapshotId, [MarshalAs(UnmanagedType.Bool)] bool bForceDismount);

    /// <summary>
    /// Queries the revert status
    /// </summary>
    [PreserveSig]
    int QueryRevertStatus(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszVolume,
        out IVssAsync ppAsync);
}

/// <summary>
/// The IVssExamineWriterMetadata interface is a COM interface that is used
/// to examine the metadata of a writer.
/// </summary>
[ComImport]
[Guid("902fcf7f-b7fd-42f8-81f1-b2e400b1e5bd")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVssExamineWriterMetadata
{
    /// <summary>
    /// Gets the identity of the writer
    /// </summary>
    [PreserveSig]
    int GetIdentity(
        out Guid pidInstance,
        out Guid pidWriter,
        [MarshalAs(UnmanagedType.BStr)] out string pbstrWriterName,
        out VssUsageType pUsage,
        out VssSourceType pSource);

    /// <summary>
    /// Gets the number of files and components
    /// </summary>
    [PreserveSig]
    int GetFileCounts(out uint pcIncludeFiles, out uint pcExcludeFiles, out uint pcComponents);

    /// <summary>
    /// Gets the specified include file
    /// </summary>
    [PreserveSig]
    int GetIncludeFile(uint iFile, out IntPtr ppFiledesc);

    /// <summary>
    /// Gets the specified exclude file
    /// </summary>
    [PreserveSig]
    int GetExcludeFile(uint iFile, out IntPtr ppFiledesc);

    /// <summary>
    /// Gets the specified component
    /// </summary>
    [PreserveSig]
    int GetComponent(uint iComponent, out IntPtr ppComponent);

    /// <summary>
    /// Gets the restore method
    /// </summary>
    [PreserveSig]
    int GetRestoreMethod(
        out int pMethod,
        [MarshalAs(UnmanagedType.BStr)] out string pbstrService,
        [MarshalAs(UnmanagedType.BStr)] out string pbstrUserProcedure,
        out int pwriterRestore,
        [MarshalAs(UnmanagedType.U1)] out bool pbRebootRequired,
        out uint pcMappings);

    /// <summary>
    /// Gets the specified alternate location mapping
    /// </summary>
    [PreserveSig]
    int GetAlternateLocationMapping(uint iMapping, out IntPtr ppFiledesc);

    /// <summary>
    /// Gets the backup schema
    /// </summary>
    [PreserveSig]
    int GetBackupSchema(out uint pdwSchemaMask);

    /// <summary>
    /// Gets the metadata document
    /// </summary>
    [PreserveSig]
    int GetDocument(out IntPtr pDoc);

    /// <summary>
    /// Saves the metadata as XML
    /// </summary>
    [PreserveSig]
    int SaveAsXML([MarshalAs(UnmanagedType.BStr)] ref string pbstrXML);

    /// <summary>
    /// Loads the metadata from XML
    /// </summary>
    [PreserveSig]
    int LoadFromXML([MarshalAs(UnmanagedType.BStr)] string bstrXML);
}

/// <summary>
/// Wraps a native IVssWMComponent object. This is a C++ (non-COM) interface
/// with no IID, so it cannot be used through COM interop (QueryInterface is
/// not implemented). Methods are invoked through the raw vtable instead.
/// The wrapper owns the native pointer and releases it on dispose.
/// </summary>
internal sealed class VssWMComponentWrapper : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetComponentInfoDelegate(IntPtr self, out IntPtr ppInfo);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FreeComponentInfoDelegate(IntPtr self, IntPtr pInfo);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetFileDelegate(IntPtr self, uint iFile, out IntPtr ppFiledesc);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ReleaseDelegate(IntPtr self);

    private readonly IntPtr _ptr;
    private readonly GetComponentInfoDelegate _getComponentInfo;
    private readonly FreeComponentInfoDelegate _freeComponentInfo;
    private readonly GetFileDelegate _getFile;
    private readonly ReleaseDelegate _release;
    private bool _disposed;

    public VssWMComponentWrapper(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            throw new ArgumentNullException(nameof(ptr));
        _ptr = ptr;
        var vtbl = Marshal.ReadIntPtr(ptr);
        // IUnknown: QueryInterface(0), AddRef(1), Release(2)
        _getComponentInfo = GetVtableDelegate<GetComponentInfoDelegate>(vtbl, 3);
        _freeComponentInfo = GetVtableDelegate<FreeComponentInfoDelegate>(vtbl, 4);
        _getFile = GetVtableDelegate<GetFileDelegate>(vtbl, 5);
        _release = GetVtableDelegate<ReleaseDelegate>(vtbl, 2);
    }

    private static T GetVtableDelegate<T>(IntPtr vtbl, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size));

    public int GetComponentInfo(out IntPtr ppInfo) => _getComponentInfo(_ptr, out ppInfo);
    public int FreeComponentInfo(IntPtr pInfo) => _freeComponentInfo(_ptr, pInfo);
    public int GetFile(uint iFile, out IntPtr ppFiledesc) => _getFile(_ptr, iFile, out ppFiledesc);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _release(_ptr);
    }
}

/// <summary>
/// Wraps a native IVssWMFiledesc object. This is a C++ (non-COM) interface
/// with no IID, so it cannot be used through COM interop (QueryInterface is
/// not implemented). Methods are invoked through the raw vtable instead.
/// The wrapper owns the native pointer and releases it on dispose.
/// </summary>
internal sealed class VssWMFiledescWrapper : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetPathDelegate(IntPtr self, out IntPtr pbstrPath);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetFilespecDelegate(IntPtr self, out IntPtr pbstrFilespec);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ReleaseDelegate(IntPtr self);

    private readonly IntPtr _ptr;
    private readonly GetPathDelegate _getPath;
    private readonly GetFilespecDelegate _getFilespec;
    private readonly ReleaseDelegate _release;
    private bool _disposed;

    public VssWMFiledescWrapper(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            throw new ArgumentNullException(nameof(ptr));
        _ptr = ptr;
        var vtbl = Marshal.ReadIntPtr(ptr);
        // IUnknown: QueryInterface(0), AddRef(1), Release(2)
        _getPath = GetVtableDelegate<GetPathDelegate>(vtbl, 3);
        _getFilespec = GetVtableDelegate<GetFilespecDelegate>(vtbl, 4);
        _release = GetVtableDelegate<ReleaseDelegate>(vtbl, 2);
    }

    private static T GetVtableDelegate<T>(IntPtr vtbl, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size));

    private static string? PtrToStringBStrAndFree(IntPtr bstr)
    {
        if (bstr == IntPtr.Zero)
            return null;
        try
        {
            return Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    public string? GetPath()
    {
        var hr = _getPath(_ptr, out var bstr);
        VssInteropUtility.ThrowIfFailed(hr, nameof(GetPath));
        return PtrToStringBStrAndFree(bstr);
    }

    public string? GetFilespec()
    {
        var hr = _getFilespec(_ptr, out var bstr);
        VssInteropUtility.ThrowIfFailed(hr, nameof(GetFilespec));
        return PtrToStringBStrAndFree(bstr);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _release(_ptr);
    }
}

/// <summary>
/// Native methods exported by VssApi.dll
/// </summary>
internal static class NativeMethods
{
    /// <summary>
    /// Creates a new IVssBackupComponents interface.
    /// The function is exported as CreateVssBackupComponentsInternal,
    /// the documented CreateVssBackupComponents is an inline C++ wrapper.
    /// </summary>
    /// <param name="ppBackup">The created interface</param>
    /// <returns>The operation result</returns>
    [DllImport("VssApi.dll", PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CreateVssBackupComponentsInternal(out IVssBackupComponents ppBackup);

    /// <summary>
    /// Frees the strings inside a VSS_SNAPSHOT_PROP structure
    /// </summary>
    /// <param name="pProp">The structure to free</param>
    [DllImport("VssApi.dll", PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern void VssFreeSnapshotProperties(ref VSS_SNAPSHOT_PROP pProp);
}

/// <summary>
/// Utility methods for the VSS interop
/// </summary>
[SupportedOSPlatform("windows")]
internal static class VssInteropUtility
{
    /// <summary>
    /// Throws an exception if the HRESULT indicates a failure
    /// </summary>
    /// <param name="hr">The HRESULT to check</param>
    /// <param name="operation">The operation being performed</param>
    public static void ThrowIfFailed(int hr, string operation)
    {
        if (hr < 0)
            throw Marshal.GetExceptionForHR(hr) ?? new COMException($"{operation} failed with HRESULT 0x{hr:X8}", hr);
    }

    /// <summary>
    /// Waits for the asynchronous operation to complete and returns the result.
    /// The asynchronous operation is released after the wait completes.
    /// </summary>
    /// <param name="asyncOperation">The asynchronous operation</param>
    /// <param name="timeoutMilliseconds">The maximum time to wait, in milliseconds</param>
    /// <param name="operation">The operation being performed, for error messages</param>
    public static void WaitAndCheck(IVssAsync asyncOperation, uint timeoutMilliseconds, string operation)
    {
        try
        {
            // Wait returns the operation result directly if it failed
            var hr = asyncOperation.Wait(timeoutMilliseconds);
            if (hr < 0)
                ThrowIfFailed(hr, operation);

            // Even when Wait succeeds, the final result is obtained from QueryStatus
            ThrowIfFailed(asyncOperation.QueryStatus(out var queryResult, IntPtr.Zero), operation);

            if (queryResult == (int)VssAsyncStatus.VSS_S_ASYNC_PENDING)
                throw new TimeoutException($"The VSS operation {operation} did not complete within the allotted time");
            if (queryResult == (int)VssAsyncStatus.VSS_S_ASYNC_CANCELLED)
                throw new OperationCanceledException($"The VSS operation {operation} was cancelled");

            ThrowIfFailed(queryResult, operation);
        }
        finally
        {
            SafeRelease(asyncOperation);
        }
    }

    /// <summary>
    /// Releases a COM object if it is one
    /// </summary>
    /// <param name="instance">The object to release</param>
    public static void SafeRelease(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance))
            Marshal.FinalReleaseComObject(instance);
    }
}
