using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Main.Strings 
{
    internal static class Controller 
    {
        public static string HashMismatchError(string filename, string recordedhash, string actualhash) { return LC.L(@"Hash mismatch on file ""{0}"", recorded hash: {1}, actual hash {2}", filename, recordedhash, actualhash); }
        public static string DownloadedFileSizeError(string filename, long actualsize, long expectedsize) { return LC.L(@"The file {0} was downloaded and had size {1} but the size was expected to be {2}", filename, actualsize, expectedsize); }
        public static string DeprecatedOptionUsedWarning(string optionname, string message) { return LC.L(@"The option {0} is deprecated: {1}", optionname, message); }
        public static string DuplicateOptionNameWarning(string optionname) { return LC.L(@"The option --{0} exists more than once, please report this to the developers", optionname); }
        public static string NoSourceFoldersError { get { return LC.L(@"No source folders specified for backup"); } }
        public static string SourceIsMissingError(string foldername) { return LC.L(@"The source folder {0} does not exist, aborting backup", foldername); }
        public static string UnsupportedBooleanValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not parse into a valid boolean, this will be treated as if it was set to ""true""", optionname, value); }
        public static string UnsupportedEnumerationValue(string optionname, string value, string[] values)  { return LC.L(@"The option --{0} does not support the value ""{1}"", supported values are: {2}", optionname, value, string.Join(", ", values)); } 
        public static string UnsupportedIntegerValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid integer", optionname, value);  }
        public static string UnsupportedOptionDisabledModuleWarning(string optionname, string modulename) { return LC.L(@"The option --{0} is not supported because the module {1} is not currently loaded", optionname, modulename); }
        public static string UnsupportedOptionWarning(string optionname) { return LC.L(@"The supplied option --{0} is not supported and will be ignored", optionname); }
        public static string UnsupportedPathValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid path", optionname, value); }
        public static string UnsupportedSizeValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid size", optionname, value); }
        public static string UnsupportedTimeValue(string optionname, string value) { return LC.L(@"The value ""{1}"" supplied to --{0} does not represent a valid time", optionname, value); } 
        public static string StartingOperationMessage(OperationMode operationname) { return LC.L(@"The operation {0} has started", operationname); }
        public static string InvalidPathError(string path, string message) { return LC.L(@"Invalid path: ""{0}"" ({1})", path, message); } 
    }

    internal static class Options 
    {
        public static string AutocleanupLong { get { return LC.L(@"If a backup is interrupted there will likely be partial files present on the backend. Using this flag, Duplicati will automatically remove such files when encountered."); } }
        public static string AutocleanupShort { get { return LC.L(@"A flag indicating that Duplicati should remove unused files"); } }
        public static string PrefixLong { get { return LC.L(@"A string used to prefix the filenames of the remote volumes, can be used to store multiple backups in the same remote folder."); } }
        public static string PrefixShort { get { return LC.L(@"Remote filename prefix"); } }
        public static string DisablefiletimecheckLong { get { return LC.L(@"The operating system keeps track of the last time a file was written. Using this information, Duplicati can quickly determine if the file has been modified. If some application deliberately modifies this information, Duplicati won't work correctly unless this flag is set."); } }
        public static string DisablefiletimecheckShort { get { return LC.L(@"Disable checks based on file time"); } }
        public static string RestorepathLong { get { return LC.L(@"By default, files will be restored in the source folders, use this option to restore to another folder"); } }
        public static string RestorepathShort { get { return LC.L(@"Restore to another folder"); } }
        public static string AllowsleepShort { get { return LC.L(@"Allow system to enter sleep power modes for inactivity during backup/restore operations (Windows only)"); } }
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
        public static string TimeLong { get { return LC.L(@"By default, Duplicati will list and restore files from the most recent backup, use this option to select another item. You may use relative times, like ""-2M"" for a backup from two months ago."); } }
        public static string TimeShort { get { return LC.L(@"The time to list/restore files"); } }
        public static string VersionLong { get { return LC.L(@"By default, Duplicati will list and restore files from the most recent backup, use this option to select another item. You may enter multiple values separated with comma, and ranges using -, e.g. ""0,2-4,7"" ."); } }
        public static string VersionShort { get { return LC.L(@"The version to list/restore files"); } }
        public static string AllversionsLong { get { return LC.L(@"When searching for files, only the most recent backup is searched. Use this option to show all previous versions too."); } }
        public static string AllversionsShort { get { return LC.L(@"Show all versions"); } }
        public static string ListprefixonlyLong { get { return LC.L(@"When searching for files, all matching files are returned. Use this option to return only the largest common prefix path."); } }
        public static string ListprefixonlyShort { get { return LC.L(@"Show largest prefix"); } }
        public static string ListfoldercontentsLong { get { return LC.L(@"When searching for files, all matching files are returned. Use this option to return only the entries found in the folder specified as filter."); } }
        public static string ListfoldercontentsShort { get { return LC.L(@"Show folder contents"); } }
        public static string RetrydelayLong { get { return LC.L(@"After a failed transmission, Duplicati will wait a short period before attempting again. This is usefull if the network drops out occasionally during transmissions."); } }
        public static string RetrydelayShort { get { return LC.L(@"Time to wait between retries"); } }
        public static string ControlfilesLong { get { return LC.L(@"Use this option to attach extra files to the newly uploaded filelists."); } }
        public static string ControlfilesShort { get { return LC.L(@"Set control files"); } }
        public static string SkipfilehashchecksLong { get { return LC.L(@"If the hash for the volume does not match, Duplicati will refuse to use the backup. Supply this flag to allow Duplicati to proceed anyway."); } }
        public static string SkipfilehashchecksShort { get { return LC.L(@"Set this flag to skip hash checks"); } }
        public static string SkipfileslargerthanLong { get { return LC.L(@"This option allows you to exclude files that are larger than the given value. Use this to prevent backups becoming extremely large."); } }
        public static string SkipfileslargerthanShort { get { return LC.L(@"Limit the size of files being backed up"); } }
        public static string TempdirShort { get { return LC.L(@"Temporary storage folder"); } }
        public static string TempdirLong { get { return LC.L(@"Duplicati will use the system default temporary folder. This option can be used to supply an alternative folder for temporary storage. Note that SQLite will always put temporary files in the system default temporary folder. Consider using the TMPDIR environment variable on Linux to set the temporary folder for both Duplicati and SQLite."); } }
        public static string ThreadpriorityLong { get { return LC.L(@"Selects another thread priority for the process. Use this to set Duplicati to be more or less CPU intensive."); } }
        public static string ThreadpriorityShort { get { return LC.L(@"Thread priority"); } }
        public static string DblocksizeLong { get { return LC.L(@"This option can change the maximum size of dblock files. Changing the size can be useful if the backend has a limit on the size of each individual file"); } }
        public static string DblocksizeShort { get { return LC.L(@"Limit the size of the volumes"); } }
        public static string DisableStreamingLong { get { return LC.L(@"Enabling this option will disallow usage of the streaming interface, which means that transfer progress bars will not show, and bandwidth throttle settings will be ignored."); } }
        public static string DisableStreamingShort { get { return LC.L(@"Disables use of the streaming transfer method"); } }
        public static string DontreadmanifestsLong { get { return LC.L(@"This option will make sure the contents of the manifest file are not read. This also implies that file hashes are not checked either. Use only for disaster recovery."); } }
        public static string DontreadmanifestsShort { get { return LC.L(@"An option that prevents verifying the manifests"); } }
        public static string CompressionmoduleLong { get { return LC.L(@"Duplicati supports plugable compression modules. Use this option to select a module to use for compression. This is only applied when creating new volumes, when reading an existing file, the filename is used to select the compression module."); } }
        public static string CompressionmoduleShort { get { return LC.L(@"Select what module to use for compression"); } }
        public static string EncryptionmoduleLong { get { return LC.L(@"Duplicati supports plugable encryption modules. Use this option to select a module to use for encryption. This is only applied when creating new volumes, when reading an existing file, the filename is used to select the encryption module."); } }
        public static string EncryptionmoduleShort { get { return LC.L(@"Select what module to use for encryption"); } }
        public static string DisablemoduleLong { get { return LC.L(@"Supply one or more module names, separated by commas to unload them"); } }
        public static string DisablemoduleShort { get { return LC.L(@"Disabled one or more modules"); } }
        public static string EnablemoduleLong { get { return LC.L(@"Supply one or more module names, separated by commas to load them"); } }
        public static string EnablemoduleShort { get { return LC.L(@"Enables one or more modules"); } }
        public static string SnapshotpolicyLong { get { return LC.L(@"This settings controls the usage of snapshots, which allows Duplicati to backup files that are locked by other programs. If this is set to ""off"", Duplicati will not attempt to create a disk snapshot. Setting this to ""auto"" makes Duplicati attempt to create a snapshot, and fail silently if that was not allowed or supported. A setting of ""on"" will also make Duplicati attempt to create a snapshot, but will produce a warning message in the log if it fails. Setting it to ""required"" will make Duplicati abort the backup if the snapshot creation fails. On windows this uses the Volume Shadow Copy Services (VSS) and requires administrative privileges. On linux this uses Logical Volume Management (LVM) and requires root privileges."); } }
        public static string SnapshotpolicyShort { get { return LC.L(@"Controls the use of disk snapshots"); } }
        public static string AsynchronousuploadfolderLong { get { return LC.L(@"The pre-generated volumes will be placed into the temporary folder by default, this option can set a different folder for placing the temporary volumes, despite the name, this also works for synchronous runs"); } }
        public static string AsynchronousuploadfolderShort { get { return LC.L(@"The path where ready volumes are placed until uploaded"); } }
        public static string AsynchronousuploadlimitLong { get { return LC.L(@"When performing asynchronous uploads, Duplicati will create volumes that can be uploaded. To prevent Duplicati from generating too many volumes, this option limits the number of pending uploads. Set to zero to disable the limit"); } }
        public static string AsynchronousuploadlimitShort { get { return LC.L(@"The number of volumes to create ahead of time"); } }
        public static string DebugoutputLong { get { return LC.L(@"Activating this option will make some error messages more verbose, which may help you track down a particular issue"); } }
        public static string DebugoutputShort { get { return LC.L(@"Enables debugging output"); } }
        public static string LogfileShort { get { return LC.L(@"Log internal information"); } }
        public static string LoglevelLong { get { return LC.L(@"Specifies the amount of log information to write into the file specified by --log-file"); } }
        public static string LoglevelShort { get { return LC.L(@"Log information level"); } }
        public static string DisableautocreatefolderLong { get { return LC.L(@"If Duplicati detects that the target folder is missing, it will create it automatically. Activate this option to prevent automatic folder creation."); } }
        public static string DisableautocreatefolderShort { get { return LC.L(@"Disables automatic folder creation"); } }
        public static string VssexcludewritersLong { get { return LC.L(@"Use this option to exclude faulty writers from a snapshot. This is equivalent to the -wx flag of the vshadow.exe tool, except that it only accepts writer class GUIDs, and not component names or instance GUIDs. Multiple GUIDs must be separated with a semicolon, and most forms of GUIDs are allowed, including with and without curly braces."); } }
        public static string VssexcludewritersShort { get { return LC.L(@"A semicolon separated list of guids of VSS writers to exclude (Windows only)"); } }
        public static string UsnpolicyLong { get { return LC.L(@"This settings controls the usage of NTFS USN numbers, which allows Duplicati to obtain a list of files and folders much faster. If this is set to ""off"", Duplicati will not attempt to use USN. Setting this to ""auto"" makes Duplicati attempt to use USN, and fail silently if that was not allowed or supported. A setting of ""on"" will also make Duplicati attempt to use USN, but will produce a warning message in the log if it fails. Setting it to ""required"" will make Duplicati abort the backup if the USN usage fails. This feature is only supported on Windows and requires administrative privileges."); } }
        public static string UsnpolicyShort { get { return LC.L(@"Controls the use of NTFS Update Sequence Numbers"); } }
        public static string DisableusndiffcheckLong { get { return LC.L(@"If USN is enabled the USN numbers are used to find all changed files since last backup. Use this option to disable the use of USN numbers, which will make Duplicati investigate all source files. This option is primarily intended for testing and should not be disabled in a production environment. If USN is not enabled, this option has no effect."); } }
        public static string DisableusndiffcheckShort { get { return LC.L(@"Disables changelist by USN numbers"); } }
        public static string DisabletimetoleranceLong { get { return LC.L(@"When deciding if a full backup should be made or an old backup should be deleted, Duplicati will adjust the times by a small fraction to ensure that minor time differences do not interfer with expected operation. If the backup is set to produce a full backup each day, and a full backup is made at, say 12:00:01, and the backup is executed again at 12:00:00, a full day has not passed and thus an incremental backup is performed. To avoid this, Duplicati inserts a 1% tolerance (max 1 hour). Use this option to disable the tolerance, and use strict time checking"); } }
        public static string DisabletimetoleranceShort { get { return LC.L(@"Deactivates tolerance when comparing times"); } }
        public static string ListverifyuploadsShort { get { return LC.L(@"Verify uploads by listing contents"); } }
        public static string SynchronousuploadLong { get { return LC.L(@"Duplicati will upload files while scanning the disk and producing volumes, which usually makes the backup faster. Use this flag to turn the behavior off, so that Duplicati will wait for each volume to complete."); } }
        public static string SynchronousuploadShort { get { return LC.L(@"Upload files synchronously"); } }
        public static string NoconnectionreuseLong { get { return LC.L(@"Duplicati will attempt to perform multiple operations on a single connection, as this avoids repeated login attempts, and thus speeds up the process. This option can be used to ensure that each operation is performed on a seperate connection"); } }
        public static string NoconnectionreuseShort { get { return LC.L(@"Do not re-use connections"); } }
        public static string DebugretryerrorsLong { get { return LC.L(@"When an error occurs, Duplicati will silently retry, and only report the number of retries. Enable this option to have the error messages displayed when a retry is performed."); } }
        public static string DebugretryerrorsShort { get { return LC.L(@"Show error messages when a retry is performed"); } }
        public static string UploadUnchangedBackupsLong { get { return LC.L(@"If no files have changed, Duplicati will not upload a backup set. If the backup data is used to verify that a backup was executed, this option will make Duplicati upload a backupset even if it is empty"); } }
        public static string UploadUnchangedBackupsShort { get { return LC.L(@"Upload empty backup files"); } }
        public static string QuotasizeLong { get { return LC.L(@"This value can be used to set a known upper limit on the amount of space a backend has. If the backend reports the size itself, this value is ignored"); } }
        public static string QuotasizeShort { get { return LC.L(@"A reported maximum storage"); } }
        public static string SymlinkpolicyShort { get { return LC.L(@"Symlink handling"); } }
        public static string SymlinkpolicyLong(string store, string ignore, string follow) { return LC.L(@"Using this option to handle symlinks different. The ""{0}"" option will simply record a symlink with its name and destination, and a restore will recreate the symlink as a link. Use the option ""{1}"" to ignore all symlinks and not store any information about them. Previous versions of Duplicati used the setting ""{2}"", which will cause symlinked files to be included and restore as normal files.", store, ignore, follow); }
        public static string HardlinkpolicyShort { get { return LC.L(@"Hardlink handling"); } }
        public static string HardlinkpolicyLong(string first, string all, string none) { return LC.L(@"Using this option to handle hardlinks (only works on Linux/OSX). The ""{0}"" option will record a hardlink ID for each hardlink to avoid storing hardlinked paths multiple times. The option ""{1}"" will ignore hardlink information, and treat each hardlink as a unique path. The option ""{2}"" will ignore all hardlinks with more than one link.", first, all, none); }
        public static string ExcludefilesattributesShort { get { return LC.L(@"Exclude files by attribute"); } }
        public static string ExcludefilesattributesLong(string[] attributes) { return LC.L(@"Use this option to exclude files with certain attributes. Use a comma separated list of attribute names to specify more that one. Possible values are: {0}", string.Join(", ", attributes)); }
        public static string VssusemappingLong { get { return LC.L(@"Activate this option to map VSS snapshots to a drive (similar to SUBST, using Win32 DefineDosDevice). This will create temporary drives that are then used to access the contents of a snapshot. This workaround can speed up file access on Windows XP."); } }
        public static string VssusemappingShort { get { return LC.L(@"Map snapshots to a drive (Windows only)"); } }
        public static string BackupnameLong { get { return LC.L(@"A display name that is attached to this backup. Can be used to identify the backup when sending mail or running scripts."); } }
        public static string BackupnameShort { get { return LC.L(@"Name of the backup"); } }
        public static string CompressionextensionfileLong(string path) { return LC.L(@"This property can be used to point to a text file where each line contains a file extension that indicates a non-compressible file. Files that have an extension found in the file will not be compressed, but simply stored in the archive. The file format ignores any lines that do not start with a period, and considers a space to indicate the end of the extension. A default file is supplied, that also serves as an example. The default file is placed in {0}.", path); }
        public static string CompressionextensionfileShort { get { return LC.L(@"Manage non-compressible file extensions"); } }
        public static string BlockhashlookupsizeLong { get { return LC.L(@"A fragment of memory is used to reduce database lookups. You should not change this value unless you get warnings in the log."); } }
        public static string BlockhashlookupsizeShort { get { return LC.L(@"Memory used by the block hash"); } }
        public static string BlocksizeLong { get { return LC.L(@"The blocksize determines how files are fragmented. Choosing a large value will cause a larger overhead on file changes, choosing a small value will cause a large overhead on storage of file lists. Note that the value cannot be changed after remote files are created."); } }
        public static string BlocksizeShort { get { return LC.L(@"Blocksize used in hashing"); } }
        public static string ChangedfilesLong { get { return LC.L(@"This option can be used to limit the scan to only files that are known to have changed. This is usually only activated in combination with a filesystem watcher that keeps track of file changes."); } }
        public static string ChangedfilesShort { get { return LC.L(@"List of files to examine for changes"); } }
        public static string DbpathLong { get { return LC.L(@"Path to the file containing the local cache of the remote file database"); } }
        public static string DbpathShort { get { return LC.L(@"Path to the local state database"); } }
        public static string DeletedfilesLong(string optionname) { return LC.L(@"This option can be used to supply a list of deleted files. This option will be ignored unless the option --{0} is also set.", optionname); }
        public static string DeletedfilesShort { get { return LC.L(@"List of deleted files"); } }
        public static string FilehashlookupsizeLong { get { return LC.L(@"A fragment of memory is used to reduce database lookups. You should not change this value unless you get warnings in the log."); } }
        public static string FilehashlookupsizeShort { get { return LC.L(@"Memory used by the file hash"); } }
        public static string DisablefilepathcacheShort { get { return LC.L(@"This option can be used to reduce the memory footprint by not keeping paths and modification timestamps in memory"); } }
        public static string DisablefilepathcacheLong { get { return LC.L(@"Reduce memory footprint by disabling in-memory lookups"); } }
        public static string StoremetadataLong { get { return LC.L(@"Stores metadata, such as file timestamps and attributes. This increases the required storage space as well as the processing time."); } }
        public static string StoremetadataShort { get { return LC.L(@"Enables storing file metadata"); } }
        public static string StoremetadataDeprecated { get { return LC.L(@"This option is no longer used as metadata is now stored by default"); } }
        public static string MetadatahashlookupsizeLong { get { return LC.L(@"A fragment of memory is used to reduce database lookups. You should not change this value unless you get warnings in the log."); } }
        public static string MetadatahashlookupsizeShort { get { return LC.L(@"Memory used by the metadata hash"); } }
        public static string NobackendverificationLong { get { return LC.L(@"If this flag is set, the local database is not compared to the remote filelist on startup. The intended usage for this option is to work correctly in cases where the filelisting is broken or unavailable."); } }
        public static string NobackendverificationShort { get { return LC.L(@"Do not query backend at startup"); } }
        public static string IndexfilepolicyLong { get { return LC.L(@"The index files are used to limit the need for downloading dblock files when there is no local database present. The more information is recorded in the index files, the faster operations can proceed without the database. The tradeoff is that larger index files take up more remote space and which may never be used."); } }
        public static string IndexfilepolicyShort { get { return LC.L(@"Determines usage of index files"); } }
        public static string ThresholdLong { get { return LC.L(@"As files are changed, some data stored at the remote destination may not be required. This option controls how much wasted space the destination can contain before being reclaimed. This value is a percentage used on each volume and the total storage."); } }
        public static string ThresholdShort { get { return LC.L(@"The maximum wasted space in percent"); } }
        public static string DryrunLong { get { return LC.L(@"This option can be used to experiment with different settings and observe the outcome without changing actual files."); } }
        public static string DryrunShort { get { return LC.L(@"Does not perform any modifications"); } }
        public static string BlockhashalgorithmLong { get { return LC.L(@"This is a very advanced option! This option can be used to select a block hash algorithm with smaller or larger hash size, for performance or storage space reasons."); } }
        public static string BlockhashalgorithmShort { get { return LC.L(@"The hash algorithm used on blocks"); } }
        public static string FilehashalgorithmLong { get { return LC.L(@"This is a very advanced option! This option can be used to select a file hash algorithm with smaller or larger hash size, for performance or storage space reasons."); } }
        public static string FilehashalgorithmShort { get { return LC.L(@"The hash algorithm used on files"); } }
        public static string NoautocompactLong { get { return LC.L(@"If a large number of small files are detected during a backup, or wasted space is found after deleting backups, the remote data will be compacted. Use this option to disable such automatic compacting and only compact when running the compact command."); } }
        public static string NoautocompactShort { get { return LC.L(@"Disable automatic compating"); } }
        public static string SmallfilesizeLong { get { return LC.L(@"When examining the size of a volume in consideration for compating, a small tolerance value is used, by default 20 percent of the volume size. This ensures that large volumes which may have a few bytes wasted space are not downloaded and rewritten."); } }
        public static string SmallfilesizeShort { get { return LC.L(@"Volume size threshold"); } }
        public static string SmallfilemaxcountLong { get { return LC.L(@"To avoid filling the remote storage with small files, this value can force grouping small files. The small volumes will always be combined when they can fill an entire volume."); } }
        public static string SmallfilemaxcountShort { get { return LC.L(@"Maximum number of small volumes"); } }
        public static string PatchwithlocalblocksLong { get { return LC.L(@"Enable this option to look into other files on this machine to find existing blocks. This is a fairly slow operation but can limit the size of downloads."); } }
        public static string PatchwithlocalblocksShort { get { return LC.L(@"Use local file data when restoring"); } }
        public static string NolocaldbShort { get { return LC.L(@"Disables the local database"); } }
        public static string NolocaldbLong { get { return LC.L(@"When listing contents or when restoring files, the local database can be skipped. This is usually slower, but can be used to verify the actual contents of the remote store"); } }
        public static string KeepversionsShort { get { return LC.L(@"Keep a number of versions"); } }
        public static string KeepversionsLong { get { return LC.L(@"Use this option to set number of versions to keep, supply -1 to keep all versions"); } }
        public static string KeeptimeShort { get { return LC.L(@"Keep all versions within a timespan"); } }
        public static string KeeptimeLong { get { return LC.L(@"Use this option to set the timespan in which backups are kept."); } }
        public static string AllowmissingsourceShort { get { return LC.L(@"Ignore missing source elements"); } }
        public static string AllowmissingsourceLong { get { return LC.L(@"Use this option to continue even if some source entries are missing."); } }
        public static string OverwriteShort { get { return LC.L(@"Overwrite files when restoring"); } }
        public static string OverwriteLong { get { return LC.L(@"Use this option to overwrite target files when restoring, if this option is not set the files will be restored with a timestamp and a number appended."); } }
        public static string VerboseShort { get { return LC.L(@"Output more progress information"); } }
        public static string VerboseLong { get { return LC.L(@"Use this option to increase the amount of output generated when running an option. Generally this option will produce a line for each file processed."); } }
        public static string UploadverificationfileShort { get { return LC.L(@"Determine if verification files are uploaded"); } }
        public static string UploadverificationfileLong { get { return LC.L(@"Use this option to upload a verification file after changing the remote storage. The file is not encrypted and contains the size and SHA256 hashes of all the remote files and can be used to verify the integrity of the files."); } }
        public static string BackendtestsamplesShort { get { return LC.L(@"The number of samples to test after a backup"); } }
        public static string BackendtestsamplesLong(string optionname) { return LC.L(@"After a backup is completed, some files are selected for verification on the remote backend. Use this option to change how many. If this value is set to 0 or the option --{0} is set, no remote files are verified", optionname); }
        public static string FullremoteverificationShort { get { return LC.L(@"Activates in-depth verification of files"); } }
        public static string FullremoteverificationLong(string optionname) { return LC.L(@"After a backup is completed, some files are selected for verification on the remote backend. Use this option to turn on full verification, which will decrypt the files and examine the insides of each volume, instead of simply verifying the external hash, If the option --{0} is set, no remote files are verified", optionname); }
        public static string FilereadbuffersizeShort { get { return LC.L(@"Size of the file read buffer"); } }
        public static string FilereadbuffersizeLong { get { return LC.L(@"Use this size to control how many bytes a read from a file before processing"); } }
        public static string AllowpassphrasechangeShort { get { return LC.L(@"Allow the passphrase to change"); } }
        public static string AllowpassphrasechangeLong { get { return LC.L(@"Use this option to allow the passphrase to change, note that this option is not permitted for a backup or repair operation"); } }
        public static string ListsetsonlyShort { get { return LC.L(@"List only filesets"); } }
        public static string ListsetsonlyLong { get { return LC.L(@"Use this option to only list filesets and avoid traversing file names and other metadata which slows down the process"); } }

        public static string SkipmetadataShort { get { return LC.L(@"Don't store metadata"); } }
        public static string SkipmetadataLong { get { return LC.L(@"Use this option to disable the storage of metadata, such as file timestamps. Disabling metadata storage will speed up the backup and restore operations, but does not affect file size much."); } }
        public static string RestorepermissionsShort { get { return LC.L(@"Restore file permissions"); } }
        public static string RestorepermissionsLong { get { return LC.L(@"By default permissions are not restored as they might prevent you from accessing your files. Use this option to restore the permissions as well."); } }
        public static string SkiprestoreverificationShort { get { return LC.L(@"Skip restored file check"); } }
        public static string SkiprestoreverificationLong { get { return LC.L(@"After restoring files, the file hash of all restored files are checked to verify that the restore was successfull. Use this option to disable the check and avoid waiting for the verification."); } }
        public static string OldmemorylookupdefaultsShort { get { return LC.L(@"Activate caches"); } }
        public static string OldmemorylookupdefaultsLong { get { return LC.L(@"Activate in-memory caches, which are now off by default"); } }
        public static string NolocalblocksShort { get { return LC.L(@"Do not use local data"); } }
        public static string NolocalblocksLong { get { return LC.L(@"Duplicati will attempt to use data from source files to minimize the amount of downloaded data. Use this option to skip this optimization and only use remote data."); } }
        public static string FullblockverificationShort { get { return LC.L(@"Check block hashes"); } }
        public static string FullblockverificationLong { get { return LC.L(@"Use this option to increase verification by checking the hash of blocks read from a volume before patching restored files with the data."); } }
        public static string LogretentionShort { get { return LC.L(@"Clean up old log data"); } }
        public static string LogretentionLong { get { return LC.L(@"Set the time after which log data will be purged from the database."); } }
        public static string RepaironlypathsShort { get { return LC.L(@"Repair database with paths"); } }
        public static string RepaironlypathsLong { get { return LC.L(@"Use this option to build a searchable local database which only contains path information. This option is usable for quickly building a database to locate certain content without needing to reconstruct all information. The resulting database can be searched, but cannot be used to restore data with."); } }
        public static string ForcelocaleShort { get { return LC.L(@"Force the locale setting"); } }
        public static string ForcelocaleLong { get { return LC.L(@"By default, your system locale and culture settings will be used. In some cases you may prefer to run with another locale, for example to get messages in another language. This option can be used to set the locale. Supply a blank string to choose the ""Invariant Cultute""."); } }
        public static string EnablepipingShort{ get { return LC.L(@"Handle downloads with threaded pipes"); } }
        public static string EnablepipingLong { get { return LC.L(@"Use this option to enable an experimental multithreaded download handling, that can significantly speed up restore operations or backup verifications depending on the hardware you're running on and your fileset. This operation mode is experimental and by default disabled."); } }
    }

    internal static class Common 
    {
        public static string InvalidCryptoSystem(string algorithm) { return LC.L(@"The cryptolibrary does not support re-usable transforms for the hash algorithm {0}", algorithm); }
        public static string InvalidHashAlgorithm(string algorithm) { return LC.L(@"The cryptolibrary does not support the hash algorithm {0}", algorithm); }
        public static string PassphraseChangeUnsupported { get { return LC.L(@"The passphrase cannot be changed for an existing backup"); } }
        public static string SnapshotFailedError(string message) { return LC.L(@"Failed to create a snapshot: {0}", message); }
    }

}
