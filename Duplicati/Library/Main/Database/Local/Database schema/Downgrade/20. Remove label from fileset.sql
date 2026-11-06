CREATE TABLE "Fileset_Temp" (
    "ID" INTEGER PRIMARY KEY,
    "OperationID" INTEGER NOT NULL,
    "VolumeID" INTEGER NOT NULL,
    "IsFullBackup" INTEGER NOT NULL,
    "Timestamp" INTEGER NOT NULL
);

INSERT INTO "Fileset_Temp" (
    "ID", "OperationID", "VolumeID", "IsFullBackup", "Timestamp"
)
SELECT
    "ID", "OperationID", "VolumeID", "IsFullBackup", "Timestamp"
FROM "Fileset";

DROP TABLE "Fileset";
ALTER TABLE "Fileset_Temp" RENAME TO "Fileset";
