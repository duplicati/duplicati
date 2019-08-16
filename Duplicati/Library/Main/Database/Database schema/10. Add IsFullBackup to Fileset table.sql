CREATE TABLE "Fileset_Temp" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"VolumeID" INTEGER NOT NULL,
	"IsFullBackup" INTEGER NOT NULL,
	"Timestamp" INTEGER NOT NULL
);

INSERT INTO "Fileset_Temp" SELECT "ID", "OperationID", "VolumeID", 1, "Timestamp" FROM "Fileset";
DROP TABLE "Fileset";
ALTER TABLE "Fileset_Temp" RENAME TO "Fileset";

UPDATE "Version" SET "Version" = 10;
