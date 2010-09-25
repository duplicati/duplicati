-- TaskSettings becomes BackendSetting
ALTER TABLE TaskSetting RENAME TO BackendSetting;

-- TaskSetting is now extensions
CREATE TABLE "TaskExtension" (
    "ID" INTEGER PRIMARY KEY,
    "TaskID" INTEGER NULL,
    "Name" TEXT NULL,
    "Value" TEXT NULL
);

-- Copy values from the Task table
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [ID], 'Max Upload Size', [MaxUploadSize] FROM Task;
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [ID], 'Upload bandwidth', [UploadBandwidth] FROM Task;
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [ID], 'Download Bandwidth', [DownloadBandwidth] FROM Task;
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [ID], 'Volume Size', [VolumeSize] FROM Task;
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [ID], 'Thread Priority', [ThreadPriority] FROM Task;
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [ID], 'Async Transfer', [AsyncTransfer] FROM Task;
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [ID], 'Include Setup', [IncludeSetup] FROM Task;
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [ID], 'Ignore Timestamps', [IgnoreTimestamps] FROM Task;

-- Move FileTimeSeparator into extensions
INSERT INTO TaskExtension ([TaskID], [Name], [Value]) SELECT [TaskID], 'File Time Seperator', Value FROM BackendSetting WHERE [Name] = 'TimeSeparator';
DELETE FROM BackendSetting WHERE [Name] = 'TimeSeparator';

-- Create a reduced Task table
CREATE TABLE "TempTask" (
    "ID" INTEGER PRIMARY KEY,
    "ScheduleID" INTEGER NULL,
    "Service" TEXT NULL,
    "Encryptionkey" TEXT NULL,
    "Signaturekey" TEXT NULL,
    "SourcePath" TEXT NULL,
    "KeepFull" INTEGER NULL,
    "KeepTime" TEXT NULL,
	"FullAfter" TEXT NULL,
	"GPGEncryption" BOOLEAN NULL,
	"IncludeSetup" BOOLEAN NULL
);

-- Copy values
INSERT INTO TempTask ([ID], [ScheduleID], [Service], [EncryptionKey], [SignatureKey], [SourcePath], [KeepFull], [KeepTime], [FullAfter], [GPGEncryption], [IncludeSetup]) SELECT [ID], [ScheduleID], [Service], [EncryptionKey], [SignatureKey], [SourcePath], [KeepFull], [KeepTime], [FullAfter], [GPGEncryption], [IncludeSetup] FROM Task;
DROP TABLE Task;

-- Use the new reduced table
ALTER TABLE "TempTask" RENAME TO "Task";

-- Add the override table
CREATE TABLE "TaskOverride" (
    "ID" INTEGER PRIMARY KEY,
    "TaskID" INTEGER NULL,
    "Name" TEXT NULL,
    "Value" TEXT NULL
);

-- Reduce database size
VACUUM;