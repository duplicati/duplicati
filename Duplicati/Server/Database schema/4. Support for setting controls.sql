-- Refactor the GPGEncryption column to now use EncryptionModule instead
ALTER TABLE "Task" ADD COLUMN "EncryptionModule" TEXT NULL;
ALTER TABLE "Task" ADD COLUMN "CompressionModule" TEXT NULL;

UPDATE "Task" SET "EncryptionModule"="aes";
UPDATE "Task" SET "EncryptionModule"="gpg" WHERE "GPGEncryption" = 1;

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
	"EncryptionModule" TEXT NULL,
	"CompressionModule" TEXT NULL,
	"IncludeSetup" BOOLEAN NULL
);

INSERT INTO TempTask ([ID], [ScheduleID], [Service], [EncryptionKey], [SignatureKey], [SourcePath], [KeepFull], [KeepTime], [FullAfter], [EncryptionModule], [CompressionModule], [IncludeSetup]) SELECT [ID], [ScheduleID], [Service], [EncryptionKey], [SignatureKey], [SourcePath], [KeepFull], [KeepTime], [FullAfter], [EncryptionModule], [CompressionModule], [IncludeSetup] FROM Task;

DROP TABLE "Task";
ALTER TABLE "TempTask" RENAME TO "Task";

-- SettingExtension is now supported
CREATE TABLE "SettingExtension" (
    "ID" INTEGER PRIMARY KEY,
    "SettingKey" TEXT NULL,
    "Name" TEXT NULL,
    "Value" TEXT NULL
);

-- Add the compression setting table
CREATE TABLE "CompressionSetting" (
    "ID" INTEGER PRIMARY KEY,
    "TaskID" INTEGER NULL,
    "Name" TEXT NULL,
    "Value" TEXT NULL
);

-- Add the encryption setting table
CREATE TABLE "EncryptionSetting" (
    "ID" INTEGER PRIMARY KEY,
    "TaskID" INTEGER NULL,
    "Name" TEXT NULL,
    "Value" TEXT NULL
);

-- Use the GPG indicator to set the compression module value
UPDATE "ApplicationSetting" SET [Name] = "Encryption module used with common password" WHERE [Name] LIKE "Use PGP with common password";
UPDATE "ApplicationSetting" SET [Value] = "aes" WHERE [Name] LIKE "Encryption module used with common password" AND [Value] LIKE "False";
UPDATE "ApplicationSetting" SET [Value] = "gpg" WHERE [Name] LIKE "Encryption module used with common password" AND [Value] LIKE "True";

-- Move the SFTP path into the controls settings, instead of the application settings
INSERT INTO "SettingExtension" ([SettingKey], [Name], [Value]) SELECT "ssh-settings", [Name], [Value] FROM "ApplicationSetting" WHERE [Name] LIKE "SFTP Path";
DELETE FROM "ApplicationSetting" WHERE [Name] LIKE "SFTP Path";

-- Move the GPG path into the controls settings, instead of the application settings, fixing the PGP -> GPG mistake on the way
INSERT INTO "SettingExtension" ([SettingKey], [Name], [Value]) SELECT "gpg-settings", "GPG Path", [Value] FROM "ApplicationSetting" WHERE [Name] LIKE "PGP Path";
DELETE FROM "ApplicationSetting" WHERE [Name] LIKE "PGP Path";

VACUUM;