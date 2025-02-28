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
using System;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Main.Strings
{
    internal static class Controller
    {
        public static string HashMismatchError(string filename, string recordedhash, string actualhash) { return LC.L(@"Hash mismatch on file ""{0}"", recorded hash: {1}, actual hash {2}", filename, recordedhash, actualhash); }
        public static string DownloadedFileSizeError(string filename, long actualsize, long expectedsize) { return LC.L(@"The file {0} was downloaded and had size {1} but the size was expected to be {2}", filename, actualsize, expectedsize); }
        public static string DeprecatedOptionUsedWarning(string optionname, string message) { return LC.L(@"The option --{0} has been deprecated: {1}", optionname, message); }
        public static string DuplicateOptionNameWarning(string optionname) { return LC.L(@"The option --{0} exists more than once. Please report this to the developers", optionname); }
        public static string NoSourceFoldersError { get { return LC.L(@"No source folders specified for backup"); } }
        public static string SourceIsMissingError(string foldername) { return LC.L(@"Backup aborted since the source path {0} does not exist. Please verify that the source path exists, or remove the source path from the backup configuration, or set the allow-missing-source option.", foldername); }
        public static string SourceUnauthorizedError(string foldername) { return LC.L(@"Unauthorized to access source folder {0}, aborting backup", foldername); }
        public static string UnsupportedBooleanValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not parse into a valid boolean. This will be treated as if it was set to ""true""", optionname, value); }
        public static string UnsupportedEnumerationValue(string optionname, string value, string[] values) { return LC.L(@"The option --{0} does not support the value ""{1}"". Supported values are: {2}", optionname, value, string.Join(", ", values)); }
        public static string UnsupportedFlagsValue(string optionname, string value, string[] values) { return LC.L(@"The option --{0} does not support the value ""{1}"". Supported flag values are: {2}", optionname, value, string.Join(", ", values)); }
        public static string UnsupportedIntegerValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid integer", optionname, value); }
        public static string UnsupportedOptionDisabledModuleWarning(string optionname, string modulename) { return LC.L(@"The option --{0} is not supported because the module {1} is not currently loaded", optionname, modulename); }
        public static string UnsupportedOptionWarning(string optionname) { return LC.L(@"The supplied option --{0} is not supported and will be ignored", optionname); }
        public static string UnsupportedPathValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid path", optionname, value); }
        public static string UnsupportedSizeValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid size", optionname, value); }
        public static string UnsupportedTimeValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid time", optionname, value); }
        public static string StartingOperationMessage(OperationMode operationname) { return LC.L(@"The operation {0} has started", operationname); }
        public static string CompletedOperationMessage(OperationMode operationname) { return LC.L(@"The operation {0} has completed", operationname); }
        public static string FailedOperationMessage(OperationMode operationname, string errormessage) { return LC.L(@"The operation {0} has failed with error: {1}", operationname, errormessage); }
        public static string InvalidPathError(string path, string message) { return LC.L(@"Invalid path: ""{0}"" ({1})", path, message); }
        public static string FailedForceLocaleError(string exMsg) { return LC.L(@"Failed to apply 'force-locale' setting. Please try to update .NET-Framework. Exception was: ""{0}"" ", exMsg); }
        public static string SourceVolumeNameInvalidError(string filename) { return LC.L(@"The source {0} uses an invalid volume name, aborting backup", filename); }
        public static string SourceVolumeNameNotFoundError(string filename, Guid volumeGuid) { return LC.L(@"The source {0} is on volume {1}, which could not be found, aborting backup", filename, volumeGuid); }
        public static string NonQualifiedSizeValue(string optionname, string value) { return LC.L(@"The size ""{1}"" supplied to --{0} does not have a multiplier (b, kb, mb, etc). A multiplier is recommended to avoid unexpected changes if the program is updated.", optionname, value); }
    }

    internal static class Options
    {
        public static string AutocleanupLong { get { return LC.L(@"If a backup is interrupted there will likely be partial files present on the backend. Using this option, Duplicati will automatically remove such files when encountered."); } }
        public static string AutocleanupShort { get { return LC.L(@"Remove unused files"); } }
        public static string PrefixLong { get { return LC.L(@"A string used to prefix the filenames of the remote volumes, can be used to store multiple backups in the same remote folder. The prefix cannot contain a hyphen (-), but can contain all other characters allowed by the remote storage."); } }
        public static string PrefixShort { get { return LC.L(@"Remote filename prefix"); } }
        public static string DisablefiletimecheckLong { get { return LC.L(@"The operating system keeps track of the last time a file was written. Using this information, Duplicati can quickly determine if the file has been modified. If some application deliberately modifies this information, Duplicati won't work correctly unless this option is set."); } }
        public static string DisablefiletimecheckShort { get { return LC.L(@"Disable checks based on file time"); } }
        public static string RestorepathLong { get { return LC.L(@"By default, files will be restored in the source folders. Use this option to restore to another folder."); } }
        public static string RestorepathShort { get { return LC.L(@"Restore to another folder"); } }
        public static string AllowsleepLong { get { return LC.L(@"Allow system to enter sleep power modes for inactivity during backup/restore operations (Windows/OSX only)"); } }
        public static string AllowsleepShort { get { return LC.L(@"Toggle system sleep mode"); } }
        public static string ThrottledownloadLong { get { return LC.L(@"By setting this value you can limit how much bandwidth Duplicati consumes for downloads. Setting this limit can make the backups take longer, but will make Duplicati less intrusive."); } }
        public static string ThrottledownloadShort { get { return LC.L(@"Max number of kilobytes to download pr. second"); } }
        public static string ThrottleuploadLong { get { return LC.L(@"By setting this value you can limit how much bandwidth Duplicati consumes for uploads. Setting this limit can make the backups take longer, but will make Duplicati less intrusive."); } }
        public static string ThrottleuploadShort { get { return LC.L(@"Max number of kilobytes to upload pr. second"); } }
        public static string NoencryptionLong { get { return LC.L(@"If you store the backups on a local disk, and prefer that they are kept unencrypted, you can turn of encryption completely by using this switch."); } }
        public static string NoencryptionShort { get { return LC.L(@"Disable encryption"); } }
        public static string NumberofretriesLong { get { return LC.L(@"If an upload or download fails, Duplicati will retry a number of times before failing. Use this to handle unstable network connections better."); } }
        public static string NumberofretriesShort { get { return LC.L(@"Number of times to retry a failed transmission"); } }
        public static string PassphraseLong { get { return LC.L(@"Supply a passphrase that Duplicati will use to encrypt the backup volumes, making them unreadable without the passphrase. This variable can also be supplied through the environment variable PASSPHRASE."); } }
        public static string PassphraseShort { get { return LC.L(@"Passphrase used to encrypt backups"); } }
        public static string TimeLong { get { return LC.L(@"By default, Duplicati will list and restore files from the most recent backup. Use this option to select another item. You may use relative times, like ""-2M"" for a backup from two months ago."); } }
        public static string TimeShort { get { return LC.L(@"The time to list/restore files"); } }
        public static string VersionLong { get { return LC.L(@"By default, Duplicati will list and restore files from the most recent backup. Use this option to select another item. You may enter multiple values separated with comma, and ranges using -, e.g. ""0,2-4,7"" ."); } }
        public static string VersionShort { get { return LC.L(@"The version to list/restore files"); } }
        public static string AllversionsLong { get { return LC.L(@"When searching for files, only the most recent backup is searched. Use this option to show all previous versions too."); } }
        public static string AllversionsShort { get { return LC.L(@"Show all versions"); } }
        public static string ListprefixonlyLong { get { return LC.L(@"When searching for files, all matching files are returned. Use this option to return only the largest common prefix path."); } }
        public static string ListprefixonlyShort { get { return LC.L(@"Show largest prefix"); } }
        public static string ListfoldercontentsLong { get { return LC.L(@"When searching for files, all matching files are returned. Use this option to return only the entries found in the folder specified as filter."); } }
        public static string ListfoldercontentsShort { get { return LC.L(@"Show folder contents"); } }
        public static string RetrydelayLong { get { return LC.L(@"After a failed transmission, Duplicati will wait a short period before attempting again. This is useful if the network drops out occasionally during transmissions."); } }
        public static string RetrydelayShort { get { return LC.L(@"Time to wait between retries"); } }
        public static string RetrywithexponentialbackoffLong { get { return LC.L(@"After a failed transmission, Duplicati will wait a short period before attempting again. This period is controlled by the retry-delay option. Use this option to double that period after each consecutive failure."); } }
        public static string RetrywithexponentialbackoffShort { get { return LC.L(@"Exponential backoff for backend errors"); } }
        public static string ControlfilesLong { get { return LC.L(@"Use this option to attach extra files to the newly uploaded filelists."); } }
        public static string ControlfilesShort { get { return LC.L(@"Set control files"); } }
        public static string SkipfilehashchecksLong { get { return LC.L(@"If the hash for the volume does not match, Duplicati will refuse to use the backup. Activate this option to allow Duplicati to proceed anyway."); } }
        public static string SkipfilehashchecksShort { get { return LC.L(@"Skip hash checks"); } }
        public static string SkipfileslargerthanLong { get { return LC.L(@"This option allows you to exclude files that are larger than the given value. Use this to prevent backups becoming extremely large."); } }
        public static string SkipfileslargerthanShort { get { return LC.L(@"Limit the size of files being backed up"); } }
        public static string TempdirLong { get { return LC.L(@"Use this option to supply an alternative folder for temporary storage. By default the system default temporary folder is used. Note that also SQLite will put temporary files in this temporary folder."); } }
        public static string TempdirShort { get { return LC.L(@"Temporary storage folder"); } }
        public static string ThreadpriorityDeprecated { get { return LC.L(@"The option --thread-priority has no effect, use the operating system controls to set the process priority"); } }
        public static string ThreadpriorityLong { get { return LC.L(@"Select another thread priority for the process. Use this to set Duplicati to be more or less CPU intensive."); } }
        public static string ThreadpriorityShort { get { return LC.L(@"Thread priority"); } }
        public static string DblocksizeLong { get { return LC.L(@"This option can change the maximum size of dblock files. Changing the size can be useful if the backend has a limit on the size of each individual file."); } }
        public static string DblocksizeShort { get { return LC.L(@"Limit the size of the volumes"); } }
        public static string DisableStreamingLong { get { return LC.L(@"Use this option to disallow usage of the streaming interface, which means that transfer progress bars will not show, and bandwidth throttle settings will be ignored."); } }
        public static string DisableStreamingShort { get { return LC.L(@"Disable use of the streaming transfer method"); } }
        public static string ReadWriteTimeoutShort { get { return LC.L(@"Set the read/write timeout for the connection"); } }
        public static string ReadWriteTimeoutLong { get { return LC.L(@"The read/write timeout is the maximum amount of time to wait for any activity during a transfer. If no activity is detected for this period, the connection is considered broken and the transfer is aborted. Set to 0s to disabled"); } }
        public static string DontreadmanifestsLong { get { return LC.L(@"Use this option to make sure the contents of the manifest file are not read. This also implies that file hashes are not checked either. Use only for disaster recovery."); } }
        public static string DontreadmanifestsShort { get { return LC.L(@"Disable manifests verification"); } }
        public static string CompressionmoduleLong { get { return LC.L(@"Duplicati supports pluggable compression modules. Use this option to select a module to use for compression. This is only applied when creating new volumes, when reading an existing file, the filename is used to select the compression module."); } }
        public static string CompressionmoduleShort { get { return LC.L(@"Select what module to use for compression"); } }
        public static string EncryptionmoduleLong { get { return LC.L(@"Duplicati supports pluggable encryption modules. Use this option to select a module to use for encryption. This is only applied when creating new volumes, when reading an existing file, the filename is used to select the encryption module."); } }
        public static string EncryptionmoduleShort { get { return LC.L(@"Select what module to use for encryption"); } }
        public static string DisablemoduleLong { get { return LC.L(@"Supply one or more module names, separated by commas to unload them."); } }
        public static string DisablemoduleShort { get { return LC.L(@"Disable one or more modules"); } }
        public static string EnablemoduleLong { get { return LC.L(@"Supply one or more module names, separated by commas to load them."); } }
        public static string EnablemoduleShort { get { return LC.L(@"Enable one or more modules"); } }
        public static string SnapshotpolicyLong { get { return LC.L(@"This setting controls the usage of snapshots, which allows Duplicati to backup files that are locked by other programs. If this is set to ""off"", Duplicati will not attempt to create a disk snapshot. Setting this to ""auto"" makes Duplicati attempt to create a snapshot, and fail silently if that was not allowed or supported (note that the OS may still log system warnings). A setting of ""on"" will also make Duplicati attempt to create a snapshot, but will produce a warning message in the log if it fails. Setting it to ""required"" will make Duplicati abort the backup if the snapshot creation fails. On windows this uses the Volume Shadow Copy Services (VSS) and requires administrative privileges. On Linux this uses Logical Volume Management (LVM) and requires root privileges."); } }
        public static string SnapshotpolicyShort { get { return LC.L(@"Control the use of disk snapshots"); } }
        public static string SnapshotproviderLong { get { return LC.L(@"The snapshot provider implementation for Windows. The AlphaVSS is the most feature complete, but is not supported on Arm64. The WMIC based snapshot has less features, but is more portable. On Linux, only LVM is supported"); } }
        public static string SnapshotproviderShort { get { return LC.L(@"The snapshot provider implementation to use"); } }
        public static string AsynchronousuploadfolderLong { get { return LC.L(@"The pre-generated volumes will be placed into the temporary folder by default. This option can set a different folder for placing the temporary volumes. Despite the name, this also works for synchronous runs."); } }
        public static string AsynchronousuploadfolderShort { get { return LC.L(@"The path where ready volumes are placed until uploaded"); } }
        public static string AsynchronousuploadlimitLong { get { return LC.L(@"When performing asynchronous uploads, Duplicati will create volumes that can be uploaded. To prevent Duplicati from generating too many volumes, this option limits the number of pending uploads. Set to zero to disable the limit.  The volume(s) that are being created are not counted in this limit. Use the option --concurrency-compressors=1 to limit the number of volumes being created."); } }
        public static string AsynchronousuploadlimitShort { get { return LC.L(@"The number of volumes to create ahead of time"); } }
        public static string AsynchronousconcurrentuploadlimitLong { get { return LC.L(@"When performing asynchronous uploads, the maximum number of concurrent uploads allowed. Set to zero to disable the limit."); } }
        public static string AsynchronousconcurrentuploadlimitShort { get { return LC.L(@"The number of concurrent uploads allowed"); } }
        public static string DebugoutputLong { get { return LC.L(@"Activate this option to make some error messages more verbose, which may help you track down a particular issue."); } }
        public static string DebugoutputShort { get { return LC.L(@"Enable debugging output"); } }
        public static string LogfileLong { get { return LC.L(@"Log information to the file specified."); } }
        public static string LogfileShort { get { return LC.L(@"Log internal information to a file"); } }
        public static string LoglevelLong { get { return LC.L(@"Specify the amount of log information to write into the file specified by the option --{0}.", "log-file"); } }
        public static string LoglevelShort { get { return LC.L(@"Log information level"); } }
        public static string LogLevelDeprecated(string option1, string option2) { return LC.L("Use the options --{0} and --{1} instead.", option1, option2); }
        public static string DisableautocreatefolderLong { get { return LC.L(@"If Duplicati detects that the target folder is missing, it will create it automatically. Activate this option to prevent automatic folder creation."); } }
        public static string DisableautocreatefolderShort { get { return LC.L(@"Disable automatic folder creation"); } }
        public static string VssexcludewritersLong { get { return LC.L(@"Use this option to exclude faulty writers from a snapshot. This is equivalent to the -wx flag of the vshadow.exe tool, except that it only accepts writer class GUIDs, and not component names or instance GUIDs. Multiple GUIDs must be separated with a semicolon, and most forms of GUIDs are allowed, including with and without curly braces."); } }
        public static string VssexcludewritersShort { get { return LC.L(@"A semicolon separated list of guids of VSS writers to exclude (Windows only)"); } }
        public static string UsnpolicyLong { get { return LC.L(@"This setting controls the usage of NTFS USN numbers, which allows Duplicati to obtain a list of files and folders much faster. If this is set to ""off"", Duplicati will not attempt to use USN. Setting this to ""auto"" makes Duplicati attempt to use USN, and fail silently if that was not allowed or supported. A setting of ""on"" will also make Duplicati attempt to use USN, but will produce a warning message in the log if it fails. Setting it to ""required"" will make Duplicati abort the backup if the USN usage fails. This feature is only supported on Windows and requires administrative privileges."); } }
        public static string UsnpolicyShort { get { return LC.L(@"Control the use of NTFS Update Sequence Numbers"); } }
        public static string IgnoreadvisorylockingShort { get { return LC.L(@"Ignore advisory locking"); } }
        public static string IgnoreadvisorylockingLong { get { return LC.L(@"When reading files Duplicati can skip files that are marked locked by another application to ensure consistency. This flag can disable the check and perform optimistic reads of locked files."); } }
        public static string DisabletimetoleranceLong { get { return LC.L(@"When matching timestamps, Duplicati will adjust the times by a small fraction to ensure that minor time differences do not cause unexpected updates. If the option --{0} is set to keep a week of backups, and the backup is made the same time each week, it is possible that the clock drifts slightly, such that full week has just passed, causing Duplicati to delete the older backup earlier than expected. To avoid this, Duplicati inserts a 1% tolerance (max 1 hour). Use this option to disable the tolerance, and use strict time checking.", "keep-time"); } }
        public static string DisabletimetoleranceShort { get { return LC.L(@"Deactivate tolerance when comparing times"); } }
        public static string ListverifyuploadsLong { get { return LC.L(@"Use this option to verify uploads by listing contents."); } }
        public static string ListverifyuploadsShort { get { return LC.L(@"Verify uploads by listing contents"); } }
        public static string SynchronousuploadLong { get { return LC.L(@"Disables uploading multiple files concurrently to preserve bandwith. This will have the same effect as setting --asynchronous-upload-limit=1 but additionally wait for related uploads. The volume that is being created is not counted in the upload limit."); } }
        public static string SynchronousuploadShort { get { return LC.L(@"Upload files synchronously"); } }
        public static string NoconnectionreuseLong { get { return LC.L(@"Duplicati will attempt to perform multiple operations on a single connection, as this avoids repeated login attempts, and thus speeds up the process. Use this option to ensure that each operation is performed on a seperate connection."); } }
        public static string NoconnectionreuseShort { get { return LC.L(@"Do not re-use connections"); } }
        public static string DebugretryerrorsLong { get { return LC.L(@"When an error occurs, Duplicati will silently retry, and only report the number of retries. Enable this option to have the error messages displayed when a retry is performed."); } }
        public static string DebugretryerrorsShort { get { return LC.L(@"Show error messages when a retry is performed"); } }
        public static string UploadUnchangedBackupsLong { get { return LC.L(@"If no files have changed, Duplicati will not upload a backup set. If the backup data is used to verify that a backup was executed, this option will make Duplicati upload a backupset even if it is empty."); } }
        public static string UploadUnchangedBackupsShort { get { return LC.L(@"Upload empty backup files"); } }
        public static string QuotasizeLong { get { return LC.L(@"Set a limit to the amount of storage used on the backend (by this backup). This is in addition to the full backend quota, if available. Note: Backups will continue past the quota. This only creates warnings and error messages."); } }
        public static string QuotasizeShort { get { return LC.L(@"Limit storage use"); } }
        public static string QuotaWarningThresholdLong { get { return LC.L(@"Set a threshold for when to warn about the backend quota being nearly exceeded. It is given as a percentage, and a warning is generated if the amount of available quota is less than this percentage of the total backup size. If the backend does not report the quota information, this value will be ignored."); } }
        public static string QuotaWarningThresholdShort { get { return LC.L(@"Threshold for warning about low quota"); } }
        public static string QuotaDisableLong(string optionname) { return LC.L(@"Disable the quota reported by the backend. The option --{0} can still be used to set a manual quota", optionname); }
        public static string QuotaDisableShort { get { return LC.L(@"Disable backend quota"); } }
        public static string SymlinkpolicyLong(string store, string ignore, string follow) { return LC.L(@"Use this option to handle symlinks differently. The ""{0}"" option will simply record a symlink with its name and destination, and a restore will recreate the symlink as a link. Use the option ""{1}"" to ignore all symlinks and not store any information about them. The option ""{2}"" will cause the symlinked target to be backed up and restored as a normal file with the symlink name. Early versions of Duplicati did not support this option and behaved as if ""{2}"" was specified.", store, ignore, follow); }
        public static string SymlinkpolicyShort { get { return LC.L(@"Symlink handling"); } }
        public static string HardlinkpolicyLong(string first, string all, string none) { return LC.L(@"Use this option to handle hardlinks (only works on Linux/OSX). The ""{0}"" option will record a hardlink ID for each hardlink to avoid storing hardlinked paths multiple times. The option ""{1}"" will ignore hardlink information, and treat each hardlink as a unique path. The option ""{2}"" will ignore all hardlinks with more than one link.", first, all, none); }
        public static string HardlinkpolicyShort { get { return LC.L(@"Hardlink handling"); } }
        public static string ExcludefilesattributesLong(string[] attributes) { return LC.L(@"Use this option to exclude files with certain attributes. Use a comma separated list of attribute names to specify more than one. Possible values are: {0}.", string.Join(", ", attributes)); }
        public static string ExcludefilesattributesShort { get { return LC.L(@"Exclude files by attribute"); } }
        public static string VssusemappingLong { get { return LC.L(@"Activate this option to map VSS snapshots to a drive (similar to SUBST, using Win32 DefineDosDevice). This will create temporary drives that are then used to access the contents of a snapshot. This workaround can speed up file access on Windows XP."); } }
        public static string VssusemappingShort { get { return LC.L(@"Map snapshots to a drive (Windows only)"); } }
        public static string BackupnameLong { get { return LC.L(@"A display name that is attached to this backup. This can be used to identify the backup when sending mail or running scripts."); } }
        public static string BackupnameShort { get { return LC.L(@"Name of the backup"); } }
        public static string BackupidLong { get { return LC.L(@"A unique identification for this backup. This can be used to identify the backup when sending mail or running scripts."); } }
        public static string BackupidShort { get { return LC.L(@"Backup ID"); } }
        public static string MachineidLong { get { return LC.L(@"A unique identification of the machine running the backup. This can be used to identify the machine when sending mail or running scripts."); } }
        public static string MachineidShort { get { return LC.L(@"Machine ID"); } }
        public static string MachinenameLong { get { return LC.L(@"The name of the machine running the backup. This can be used to identify the machine when sending mail or running scripts."); } }
        public static string MachinenameShort { get { return LC.L(@"Machine name"); } }
        public static string NextscheduledrunShort { get { return LC.L(@"The time of the next scheduled run"); } }
        public static string NextscheduledrunLong { get { return LC.L(@"This property is a reporting option and does not affect the actual scheduled time. Use this option to inform a reporting destination about the next expected time the backup will run."); } }
        public static string CompressionextensionfileLong(string path) { return LC.L(@"Use this option to point to a text file where each line contains a file extension that indicates a non-compressible file. Files that have an extension found in the file will not be compressed, but simply stored in the archive. The file format ignores any lines that do not start with a period, and considers a space to indicate the end of the extension. A default file is supplied, that also serves as an example. The default file is placed in {0}.", path); }
        public static string CompressionextensionfileShort { get { return LC.L(@"Manage non-compressible file extensions"); } }
        public static string BlocksizeLong { get { return LC.L(@"The block size determines how files are fragmented. Choosing a large value will cause a larger overhead on file changes, choosing a small value will cause a large overhead on storage of file lists. Note that the value cannot be changed after remote files are created."); } }
        public static string BlocksizeShort { get { return LC.L(@"Block size used in hashing"); } }
        public static string ChangedfilesLong { get { return LC.L(@"Use this option to limit the scan to only files that are known to have changed. This is usually only activated in combination with a filesystem watcher that keeps track of file changes."); } }
        public static string ChangedfilesShort { get { return LC.L(@"List of files to examine for changes"); } }
        public static string DbpathLong { get { return LC.L(@"Path to the file containing the local cache of the remote file database."); } }
        public static string DbpathShort { get { return LC.L(@"Path to the local state database"); } }
        public static string DeletedfilesLong(string optionname) { return LC.L(@"Use this option to supply a list of deleted files. This option will be ignored unless the option --{0} is also set.", optionname); }
        public static string DeletedfilesShort { get { return LC.L(@"List of deleted files"); } }
        public static string DisablefilepathcacheLong { get { return LC.L(@"Use this option to reduce the memory footprint by not keeping paths and modification timestamps in memory."); } }
        public static string DisablefilepathcacheShort { get { return LC.L(@"Reduce memory footprint by disabling in-memory lookups"); } }
        public static string DisablefilepathcacheDeprecated { get { return LC.L(@"The option --{0} is no longer used and has been deprecated.", "disable-filepath-cache"); } }
        public static string NobackendverificationLong { get { return LC.L(@"If this option is set, the local database is not compared to the remote filelist on startup. The intended usage for this option is to work correctly in cases where the filelisting is broken or unavailable."); } }
        public static string NobackendverificationShort { get { return LC.L(@"Do not query backend at startup"); } }
        public static string IndexfilepolicyLong { get { return LC.L(@"The index files are used to limit the need for downloading dblock files when there is no local database present. The more information is recorded in the index files, the faster operations can proceed without the database. The tradeoff is that larger index files take up more remote space and which may never be used."); } }
        public static string IndexfilepolicyShort { get { return LC.L(@"Determine usage of index files"); } }
        public static string ThresholdLong { get { return LC.L(@"As files are changed, some data stored at the remote destination may not be required. This option controls how much wasted space the destination can contain before being reclaimed. This value is a percentage used on each volume and the total storage."); } }
        public static string ThresholdShort { get { return LC.L(@"The maximum wasted space in percent"); } }
        public static string DryrunLong { get { return LC.L(@"Use this option to experiment with different settings and observe the outcome without changing actual files."); } }
        public static string DryrunShort { get { return LC.L(@"Do not perform any modifications"); } }
        public static string BlockhashalgorithmLong { get { return LC.L(@"This is a very advanced option! Use this option to select a block hash algorithm with smaller or larger hash size, for performance or storage space reasons."); } }
        public static string BlockhashalgorithmShort { get { return LC.L(@"The hash algorithm used on blocks"); } }
        public static string FilehashalgorithmLong { get { return LC.L(@"This is a very advanced option! Use this option to select a file hash algorithm with smaller or larger hash size, for performance or storage space reasons."); } }
        public static string FilehashalgorithmShort { get { return LC.L(@"The hash algorithm used on files"); } }
        public static string NoautocompactLong { get { return LC.L(@"If a large number of small files are detected during a backup, or wasted space is found after deleting backups, the remote data will be compacted. Use this option to disable such automatic compacting and only compact when running the compact command."); } }
        public static string NoautocompactShort { get { return LC.L(@"Disable automatic compacting"); } }
        public static string SmallfilesizeLong { get { return LC.L(@"When examining the size of a volume in consideration for compacting, a small tolerance value is used, by default 20 percent of the volume size. This ensures that large volumes which may have a few bytes wasted space are not downloaded and rewritten."); } }
        public static string SmallfilesizeShort { get { return LC.L(@"Volume size threshold"); } }
        public static string SmallfilemaxcountLong { get { return LC.L(@"To avoid filling the remote storage with small files, this value can force grouping small files. The small volumes will always be combined when they can fill an entire volume."); } }
        public static string SmallfilemaxcountShort { get { return LC.L(@"Maximum number of small volumes"); } }
        public static string PatchwithlocalblocksLong { get { return LC.L(@"Enable this option to look into other files on this machine to find existing blocks. This is a fairly slow operation but can limit the size of downloads."); } }
        public static string PatchwithlocalblocksShort { get { return LC.L(@"Use local file data when restoring"); } }
        public static string PatchwithlocalblocksDeprecated(string optionname) { return LC.L(@"Use the option --{0} instead.", optionname); }
        public static string NolocaldbLong { get { return LC.L(@"When listing contents or when restoring files, the local database can be skipped. This is usually slower, but can be used to verify the actual contents of the remote store."); } }
        public static string NolocaldbShort { get { return LC.L(@"Disable the local database"); } }
        public static string KeepversionsLong { get { return LC.L(@"Use this option to set number of versions to keep. Supply -1 to keep all versions."); } }
        public static string KeepversionsShort { get { return LC.L(@"Keep a number of versions"); } }
        public static string KeeptimeLong { get { return LC.L(@"Use this option to set the timespan in which backups are kept."); } }
        public static string KeeptimeShort { get { return LC.L(@"Keep all versions within a timespan"); } }
        public static string RetentionPolicyLong { get { return LC.L(@"Use this option to reduce the number of versions that are kept with increasing version age by deleting most of the old backups. The expected format is a comma separated list of colon separated time frame and interval pairs. For example the value ""7D:0s,3M:1D,10Y:2M"" means ""For 7 day keep all backups, for 3 months keep one backup every day, for 10 years one backup every 2nd month and delete every backup older than this."". This option also supports using the specifier ""U"" to indicate an unlimited time interval."); } }
        public static string RetentionPolicyShort { get { return LC.L(@"Reduce number of versions by deleting old intermediate backups"); } }
        public static string AllowmissingsourceLong { get { return LC.L(@"Use this option to continue even if some source entries are missing."); } }
        public static string AllowmissingsourceShort { get { return LC.L(@"Ignore missing source elements"); } }
        public static string OverwriteLong { get { return LC.L(@"Use this option to overwrite target files when restoring. If this option is not set, the files will be restored with a timestamp and a number appended."); } }
        public static string OverwriteShort { get { return LC.L(@"Overwrite files when restoring"); } }
        public static string VerboseLong { get { return LC.L(@"Use this option to increase the amount of output generated when running an option. Generally this option will produce a line for each file processed."); } }
        public static string VerboseShort { get { return LC.L(@"Output more progress information"); } }
        public static string VerboseDeprecated { get { return LC.L("Use the options --{0} and --{1} instead.", "log-file-log-level", "console-log-level"); } }
        public static string FullresultLong { get { return LC.L(@"Use this option to increase the amount of output generated as the result of the operation, including all filenames."); } }
        public static string FullresultShort { get { return LC.L(@"Output full results"); } }
        public static string UploadverificationfileLong { get { return LC.L(@"Use this option to upload a verification file after changing the remote storage. The file is not encrypted and contains the size and SHA256 hashes of all the remote files and can be used to verify the integrity of the files."); } }
        public static string UploadverificationfileShort { get { return LC.L(@"Determine if verification files are uploaded"); } }
        public static string BackendtestsamplesLong(string optionname) { return LC.L(@"After a backup is completed, some (dblock, dindex, dlist) files from the remote backend are selected for verification. Use this option to change how many. If the option --{0} is also provided, the number of samples tested is the maximum implied by the two options. If this value is set to 0 or the option --{1} is set, no remote files are verified.", "backup-test-percentage", optionname); }
        public static string BackendtestsamplesShort { get { return LC.L(@"The number of samples to test after a backup"); } }
        public static string BackendtestpercentageLong { get { return LC.L(@"After a backup is completed, some (dblock, dindex, dlist) files from the remote backend are selected for verification. Use this option to specify the percentage (between 0 and 100) of files to test. If the option --{0} is also provided, the number of samples tested is the maximum implied by the two options. If the option --{1} is provided, no remote files are verified.", "backup-test-samples", "no-backend-verification"); } }
        public static string BackendtestpercentageShort { get { return LC.L(@"The percentage of samples to test after a backup"); } }
        public static string FullremoteverificationLong(string optionname) { return LC.L(@"After a backup is completed, some (dblock, dindex, dlist) files from the remote backend are selected for verification. Use this option to turn on full verification, which will decrypt the files and examine the insides of each volume, instead of simply verifying the external hash. If the option --{0} is set, no remote files are verified. This option is automatically set when then verification is performed directly. ListAndIndexes is like True but only dlist and index volumes are handled.", optionname); }
        public static string FullremoteverificationShort { get { return LC.L(@"Activate in-depth verification of files"); } }
        public static string FilereadbuffersizeLong { get { return LC.L(@"Use this size to control how many bytes are read from a file before processing."); } }
        public static string FilereadbuffersizeShort { get { return LC.L(@"Size of the file read buffer"); } }
        public static string FilereadbuffersizeDeprecated { get { return LC.L(@"The option --{0} is no longer used and has been deprecated.", "file-read-buffer-size"); } }
        public static string AllowpassphrasechangeLong { get { return LC.L(@"Use this option to allow the passphrase to change. Note that this option is not permitted for a backup or repair operation."); } }
        public static string AllowpassphrasechangeShort { get { return LC.L(@"Allow the passphrase to change"); } }
        public static string ListsetsonlyLong { get { return LC.L(@"Use this option to only list filesets and avoid traversing file names and other metadata which slows down the process."); } }
        public static string ListsetsonlyShort { get { return LC.L(@"List only filesets"); } }

        public static string SkipmetadataLong { get { return LC.L(@"Use this option to disable the storage of metadata, such as file timestamps. Disabling metadata storage will speed up the backup and restore operations, but does not affect file size much."); } }
        public static string SkipmetadataShort { get { return LC.L(@"Do not store metadata"); } }
        public static string RestorepermissionsLong { get { return LC.L(@"By default permissions are not restored as they might prevent you from accessing your files. Use this option to restore the permissions as well."); } }
        public static string RestorepermissionsShort { get { return LC.L(@"Restore file permissions"); } }
        public static string SkiprestoreverificationLong { get { return LC.L(@"After restoring files, the file hash of all restored files are checked to verify that the restore was successful. Use this option to disable the check and avoid waiting for the verification."); } }
        public static string SkiprestoreverificationShort { get { return LC.L(@"Skip restored file check"); } }
        public static string NolocalblocksLong { get { return LC.L(@"Duplicati will attempt to use data from source files to minimize the amount of downloaded data. Use this option to skip this optimization and only use remote data."); } }
        public static string NolocalblocksShort { get { return LC.L(@"Do not use local data"); } }
        public static string NolocalblocksDeprecated(string alternativeOptionName) { return LC.L(@"The default is now to not use local blocks for restore. To opt-in for using local blocks, set the option --{0}.", alternativeOptionName); }
        public static string RestorewithlocalblocksLong { get { return LC.L(@"Use this option to allow Duplicati to use blocks found on disk when performing restores, instead of only using files in remote storage."); } }
        public static string RestorewithlocalblocksShort { get { return LC.L(@"Use existing data for restore"); } }

        public static string FullblockverificationLong { get { return LC.L(@"Use this option to increase verification by checking the hash of blocks read from a volume before patching restored files with the data."); } }
        public static string FullblockverificationShort { get { return LC.L(@"Check block hashes"); } }
        public static string LogretentionLong { get { return LC.L(@"Set the time after which log data will be purged from the database."); } }
        public static string LogretentionShort { get { return LC.L(@"Clean up old log data"); } }
        public static string RepaironlypathsLong { get { return LC.L(@"Use this option to build a searchable local database which only contains path information. This option is usable for quickly building a database to locate certain content without needing to reconstruct all information. The resulting database can be searched, but cannot be used to restore data with."); } }
        public static string RepaironlypathsShort { get { return LC.L(@"Repair database with paths"); } }
        public static string ForcelocaleLong { get { return LC.L(@"By default, your system locale and culture settings will be used. In some cases you may prefer to run with another locale, for example to get messages in another language. Use this option to set the locale. Supply a blank string to choose the ""Invariant Culture""."); } }
        public static string ForcelocaleShort { get { return LC.L(@"Force the locale setting"); } }
        public static string ForceActualDateLong { get { return LC.L(@"By default, dates are displayed in the calendar format, meaning ""Today"" or ""Last Thursday"". By setting this option, only the actual dates are displayed, ""Nov 12, 2018, 8:01 AM"" for example."); } }
        public static string ForceActualDateShort { get { return LC.L(@"Force the display of the actual date instead of calendar date"); } }
        public static string DisablepipingLong { get { return LC.L(@"Use this option to disable multithreaded handling of up- and downloads. That can significantly speed up backend operations depending on the hardware you're running on and the transfer rate of your backend."); } }
        public static string DisablepipingShort { get { return LC.L(@"Handle file communication with backend using threaded pipes"); } }
        public static string ConcurrencymaxthreadsLong { get { return LC.L(@"Use this option to set the maximum number of threads used. Setting this value to zero or less will dynamically balance the number of active threads to fit the hardware."); } }
        public static string ConcurrencymaxthreadsShort { get { return LC.L(@"Limit number of concurrent threads"); } }
        public static string ConcurrencyblockhashersLong { get { return LC.L(@"Use this option to set the number of processes that perform hashing of data."); } }
        public static string ConcurrencyblockhashersShort { get { return LC.L(@"Specify the number of concurrent hashing processes"); } }
        public static string ConcurrencycompressorsLong { get { return LC.L(@"Use this option to set the number of processes that perform compression of output data."); } }
        public static string ConcurrencycompressorsShort { get { return LC.L(@"Specify the number of concurrent compression processes"); } }
        public static string ConcurrencyfileprocessorsShort { get { return LC.L(@"[EXPERIMENTAL]Specify the number of concurrent files to open"); } }
        public static string ConcurrencyfileprocessorsLong { get { return LC.L(@"Use this option to set the number of concurrent files to open. This could accelerate big backups involving lot of files, such as an initial backup"); } }
        public static string DisablesyntehticfilelistLong { get { return LC.L(@"If Duplicati detects that the previous backup did not complete, it will generate a filelist that is a merge of the last completed backup and the contents that were uploaded in the incomplete backup session."); } }
        public static string DisablesyntheticfilelistShort { get { return LC.L(@"Disable synthetic filelist"); } }
        public static string CheckfiletimeonlyLong { get { return LC.L(@"This option instructs Duplicati to not look at metadata or filesize when deciding to scan a file for changes. Use this option if you have a large number of files and notice that the scanning takes a long time with unmodified files."); } }
        public static string CheckfiletimeonlyShort { get { return LC.L(@"Check only file lastmodified"); } }
        public static string DontcompressrestorepathsLong { get { return LC.L(@"When restore a subset of a backup into a new folder, the shortest possible path is used to avoid generating deep paths with empty folders. Use this option to skip this compression, such that the entire original folder structure is preserved, including upper level empty folders."); } }
        public static string DontcompressrestorepathsShort { get { return LC.L(@"Disable path compression on restore"); } }
        public static string AllowfullremovalLong { get { return LC.L(@"By default, the last fileset cannot be removed. This is a safeguard to make sure that all remote data is not deleted by a configuration mistake. Use this option to disable that protection, such that all filesets can be deleted."); } }
        public static string AllowfullremovalShort { get { return LC.L(@"Allow removing all filesets"); } }
        public static string AutoVacuumLong { get { return LC.L(@"Some operations that manipulate the local database leave unused entries behind. These entries are not deleted from a hard drive until a VACUUM operation is run. This operation saves disk space in the long run but needs to temporarily create a copy of all valid entries in the database. Setting this to true will allow Duplicati to perform VACUUM operations at its discretion."); } }
        public static string AutoVacuumShort { get { return LC.L(@"Allow automatic rebuilding of local database to save space"); } }
        public static string DisablefilescannerLong { get { return LC.L(@"When this flag is enabled, the scanner that computes the size of source files is disabled, and instead the reported size is read from the database. Using this option can speed up the backup by reducing disk access, but will give a less accurate progress indicator."); } }
        public static string DisablefilescannerShort { get { return LC.L(@"Disable the read-ahead scanner"); } }
        public static string DisablefilelistconsistencychecksLong { get { return LC.L(@"In backups with a large number of filesets, the verification can take up a large part of the backup time. If you disable the checks, make sure you run regular check commands to ensure that everything is working as expected."); } }
        public static string DisablefilelistconsistencychecksShort { get { return LC.L(@"Disable filelist consistency checks"); } }
        public static string DisableOnBatteryLong { get { return LC.L("Use this option to disable a scheduled backup if the system is detected to be running on battery power (manual or command line backups will still be run). If the detected power source is mains (e.g., AC) or unknown, then scheduled backups will proceed as normal."); } }
        public static string DisableOnBatteryShort { get { return LC.L("Disable the backup when on battery power"); } }

        public static string LogfileloglevelLong { get { return LC.L(@"Specify the amount of log information to write into the file specified by the option --{0}.", "log-file"); } }
        public static string LogfileloglevelShort { get { return LC.L(@"Log file information level"); } }
        public static string LogfilelogfiltersLong(string delimiter) { return LC.L(@"This option accepts filters that removes or includes messages regardless of their log level. Multiple filters are supported by separating with {0}. Filters are matched against the log tag and assumed to be including, unless they start with '-'. Regular expressions are supported within hard braces. Example: ""+Path*{0}+*Mail*{0}-[.*DNS]"" ", delimiter); }
        public static string LogfilelogfiltersShort { get { return LC.L(@"Apply filters to the file log data"); } }
        public static string ConsoleloglevelLong { get { return LC.L(@"Specify the amount of log information to output to the console."); } }
        public static string ConsoleloglevelShort { get { return LC.L(@"Console information level"); } }
        public static string ConsolelogfiltersLong(string delimiter) { return LogfilelogfiltersLong(delimiter); }
        public static string ConsolelogfiltersShort { get { return LC.L(@"Apply filters to the console log data"); } }

        public static string UsebackgroundiopriorityLong { get { return LC.L("This option instructs the operating system to set the current process to use the lowest IO priority level, which can make operations run slower but will interfere less with other operations running at the same time."); } }
        public static string UsebackgroundiopriorityShort { get { return LC.L("Set the process to use low IO priority"); } }

        public static string ExcludeemptyfoldersLong { get { return LC.L("Use this option to remove all empty folders from a backup."); } }
        public static string ExcludeemptyfoldersShort { get { return LC.L("Exclude empty folders"); } }
        public static string IgnorefilenamesLong { get { return LC.L("Use this option to set a filename, or list of filenames, that indicate exclusion of a folder which contains it. A common use would be to have a file named something like \".nobackup\" and place this file into folders that should not be backed up."); } }
        public static string IgnorefilenamesShort { get { return LC.L("List of filenames that exclude folders"); } }
        public static string RestoresymlinkmetadataLong { get { return LC.L("If symlink metadata is applied, it will usually mean changing the symlink target, instead of the symlink itself. For this reason, metadata is not applied to symlinks, but this option can be used to override this, such that metadata is applied to symlinks as well."); } }
        public static string RestoresymlinkmetadataShort { get { return LC.L("Apply metadata to symlinks"); } }
        public static string UnittestmodeLong { get { return LC.L("When running in unittest mode, no automatic fixes are applied, which assumes that the input data is always in perfect shape. This option is not intended for use in daily backups, but required for testing purposes to reveal potential problems."); } }
        public static string UnittestmodeShort { get { return LC.L("Activate unittest mode"); } }

        public static string ProfilealldatabasequeriesLong { get { return LC.L("To improve performance of the backups, frequent database queries are not logged by default. Enable this option to log all database queries, and remember to set either --{0}={2} or --{1}={2} to report the additional log data", "console-log-level", "log-file-log-level", nameof(Logging.LogMessageType.Profiling)); } }
        public static string ProfilealldatabasequeriesShort { get { return LC.L("Activate logging of all database queries"); } }
        public static string RebuildmissingdblockfilesLong { get { return LC.L("If dblock files are missing from the destination, you can attempt to rebuild them using local source data. However, since the local data may have changed, it may not be possible to retrieve all the required data and the process may be slow. Use this option to attempt to rebuild missing dblock files."); } }
        public static string RebuildmissingdblockfilesShort { get { return LC.L("Rebuild dblock files when missing"); } }

        public static string AutoCompactIntervalLong { get { return LC.L("The minimum amount of time that must elapse after the last compaction before another will be automatically triggered at the end of a backup job. Automatic compaction can be a long-running process and may not be desirable to run after every single backup."); } }
        public static string AutoCompactIntervalShort { get { return LC.L("Minimum time between auto compactions"); } }
        public static string AutoVacuumIntervalLong { get { return LC.L("The minimum amount of time that must elapse after the last vacuum before another will be automatically triggered at the end of a backup job. Automatic vacuum can be a long-running process and may not be desirable to run after every single backup."); } }
        public static string AutoVacuumIntervalShort { get { return LC.L("Minimum time between auto vacuums"); } }
        public static string SecretProviderShort { get { return LC.L("Secret provider to use for reading credentials"); } }
        public static string SecretProviderLong(string toolname) { return LC.L("Configures a secret provider to use for reading credentials. Use the commandline tool {0} to test the provider and see supported options. This value is interpreted as an environment variable if it starts with '$' or begins and ends with '%'.", toolname); }
        public static string SecretProviderPatternShort { get { return LC.L("Pattern for secrets"); } }
        public static string SecretProviderPatternLong { get { return LC.L("Use this option to specify a pattern for secret provider options. The pattern is used to find values that are intended to be translated by the secret provider. Patterns are treated as a prefix, with support for braces."); } }
        public static string SecretProviderCacheShort { get { return LC.L("Cache rules for the secret provider"); } }
        public static string SecretProviderCacheLong { get { return LC.L("Use this option to set the allowed caching of credentials from the secret provider. Setting a cache level may reduce the security but allow the backups to continue despite provider outages."); } }

        public static string CPUIntensityShort { get { return LC.L("CPU intensity level"); } }
        public static string CPUIntensityLong { get { return LC.L("Set the CPU intensity level to limit CPU resource utilization. A higher number translates into a higher utilization budget. E.g. 10 would mean no restrictions. Must be an integer between 1-10."); } }

        public static string RestoreCacheMaxShort { get { return LC.L("Maximum cache size for restoring files"); } }
        public static string RestoreCacheMaxLong { get { return LC.L($"Use this option to set the maximum size of the cache used for restoring files. The cache is used to store the data blocks that are downloaded from the remote storage. It assumes that the value is divisable by the block size, except for when it is 0, which disables the block cache."); } }
        public static string RestoreCacheEvictShort { get { return LC.L("Eviction ratio of the data block cache during restore"); } }
        public static string RestoreCacheEvictLong { get { return LC.L("Use this option to set the eviction ratio of the data block cache during restore. The eviction ratio is the percentage of the cache that is evicted when the cache is full. The default value is 50, which means that 50% of the cache is evicted when the cache is full."); } }
        public static string RestoreFileprocessorsShort { get { return LC.L("Number of concurrent FileProcessors processes used during restore"); } }
        public static string RestoreFileprocessorsLong { get { return LC.L("Use this option to set the number of concurrent FileProcessors processes used during restore. A FileProcessor processes one file at a time, and increasing the number of FileProcessors may improve restore performance."); } }
        public static string RestoreLegacyShort { get { return LC.L("Use legacy restore method"); } }
        public static string RestoreLegacyLong { get { return LC.L("Use this option to use the legacy restore method. The legacy restore method is slower than the new method, but may be more reliable in some cases."); } }
        public static string RestorePreallocateSizeShort { get { return LC.L("Preallocate size of restored files"); } }
        public static string RestorePreallocateSizeLong { get { return LC.L("Use this option to toggle whether to set the size of the restored files before they are written to disk. This can help to reduce fragmentation and improve performance on some filesystems."); } }
        public static string RestoreVolumeDecompressorsShort { get { return LC.L("Number of concurrent FileDecompressor processes used during restore"); } }
        public static string RestoreVolumeDecompressorsLong { get { return LC.L("Use this option to set the number of concurrent FileDecompressor processes used during restore. A FileDecompressor processes one volume at a time, and increasing the number of FileDecompressors may improve restore performance if the bottleneck is decompression."); } }
        public static string RestoreVolumeDecryptorsShort { get { return LC.L("Number of concurrent FileDecryptor processes used during restore"); } }
        public static string RestoreVolumeDecryptorsLong { get { return LC.L("Use this option to set the number of concurrent FileDecryptor processes used during restore. A FileDecryptor processes one volume at a time, and increasing the number of FileDecryptors may improve restore performance if the bottleneck is decryption."); } }
        public static string RestoreVolumeDownloadersShort { get { return LC.L("Number of concurrent FileDownloader processes used during restore"); } }
        public static string RestoreVolumeDownloadersLong { get { return LC.L("Use this option to set the number of concurrent FileDownloader processes used during restore. A FileDownloader processes one volume at a time, and increasing the number of FileDownloaders may improve restore performance if the bottleneck is downloading."); } }
        public static string RestoreChannelBufferSizeShort { get { return LC.L("Size of buffers of the channels used during restore"); } }
        public static string RestoreChannelBufferSizeLong { get { return LC.L("Use this option to set the size of the buffers of the channels used during restore. The buffers are used to allow for better asynchronous communication between the processes in the restore flow. Increasing the buffer size may improve restore performance."); } }
        public static string InternalProfilingShort { get { return LC.L("Enable internal profiling"); } }
        public static string InternalProfilingLong { get { return LC.L("Use this option to enable internal profiling. Profiling is used to measure the performance of the internal code. The profiling data is written to the log file and can be used to identify performance bottlenecks."); } }
    }

    internal static class Common
    {
        public static string InvalidCryptoSystem(string algorithm) { return LC.L(@"The cryptolibrary does not support re-usable transforms for the hash algorithm {0}", algorithm); }
        public static string InvalidHashAlgorithm(string algorithm) { return LC.L(@"The cryptolibrary does not support the hash algorithm {0}", algorithm); }
        public static string PassphraseChangeUnsupported { get { return LC.L(@"The passphrase cannot be changed for an existing backup"); } }
        public static string SnapshotFailedError(string message) { return LC.L(@"Failed to create a snapshot: {0}", message); }
    }

    internal static class BackendMananger
    {
        public static string EncryptionModuleNotFound(string name) { return LC.L(@"The encryption module {0} was not found", name); }
    }

}
