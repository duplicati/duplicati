ALTER TABLE Backup RENAME TO BackupTemp;

CREATE TABLE "Backup" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NOT NULL DEFAULT '',
    "Tags" TEXT NOT NULL,
    "TargetURL" TEXT NOT NULL,
    "DBPath" TEXT NOT NULL
);

INSERT INTO "Backup"("ID", "Name", "Description", "Tags", "TargetURL", "DBPath")
SELECT "ID", "Name", "Description", "Tags", "TargetURL", "DBPath"
FROM "BackupTemp";

DROP TABLE "BackupTemp";
