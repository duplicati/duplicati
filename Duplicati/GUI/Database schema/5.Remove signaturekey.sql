CREATE TABLE "TempTask" (
    "ID" INTEGER PRIMARY KEY,
    "ScheduleID" INTEGER NULL,
    "Service" TEXT NULL,
    "Encryptionkey" TEXT NULL,
    "SourcePath" TEXT NULL,
    "KeepFull" INTEGER NULL,
    "KeepTime" TEXT NULL,
	"FullAfter" TEXT NULL,
	"EncryptionModule" TEXT NULL,
	"CompressionModule" TEXT NULL,
	"IncludeSetup" BOOLEAN NULL
);

INSERT INTO TempTask ([ID], [ScheduleID], [Service], [EncryptionKey], [SourcePath], [KeepFull], [KeepTime], [FullAfter], [EncryptionModule], [CompressionModule], [IncludeSetup]) SELECT [ID], [ScheduleID], [Service], [EncryptionKey], [SourcePath], [KeepFull], [KeepTime], [FullAfter], [EncryptionModule], [CompressionModule], [IncludeSetup] FROM Task;

DROP TABLE "Task";
ALTER TABLE "TempTask" RENAME TO "Task";

VACUUM;