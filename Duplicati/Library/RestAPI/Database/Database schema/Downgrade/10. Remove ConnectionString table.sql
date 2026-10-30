DROP TABLE "ConnectionString";

ALTER TABLE Backup RENAME TO BackupTemp;

CREATE TABLE "Backup" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NOT NULL DEFAULT '',
    "Tags" TEXT NOT NULL,
    "TargetURL" TEXT NOT NULL,
    "DBPath" TEXT NOT NULL,
    "ExternalID" TEXT NULL
);

INSERT INTO "Backup"("ID", "Name", "Description", "Tags", "TargetURL", "DBPath", "ExternalID")
SELECT "ID", "Name", "Description", "Tags", "TargetURL", "DBPath", "ExternalID"
FROM "BackupTemp";

DROP TABLE "BackupTemp";
