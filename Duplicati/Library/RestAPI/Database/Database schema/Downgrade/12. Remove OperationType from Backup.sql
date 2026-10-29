-- Downgrade: Remove the OperationType column from the Backup table.
ALTER TABLE "Backup" RENAME TO "BackupTemp";

CREATE TABLE "Backup" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NOT NULL DEFAULT '',
    "Tags" TEXT NOT NULL,
    "TargetURL" TEXT NOT NULL,
    "DBPath" TEXT NOT NULL,
    "ExternalID" TEXT NULL,
    "ConnectionStringID" INTEGER NOT NULL DEFAULT -1
);

INSERT INTO "Backup"("ID", "Name", "Description", "Tags", "TargetURL", "DBPath", "ExternalID", "ConnectionStringID")
SELECT "ID", "Name", "Description", "Tags", "TargetURL", "DBPath", "ExternalID", "ConnectionStringID"
FROM "BackupTemp";

DROP TABLE "BackupTemp";
