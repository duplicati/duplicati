
CREATE TABLE "RemoteVolume_Temp" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"Name" TEXT NOT NULL,
	"Type" TEXT NOT NULL,
	"Size" INTEGER NULL,
	"Hash" TEXT NULL,
	"State" TEXT NOT NULL,
	"VerificationCount" INTEGER NOT NULL,
	"DeleteGraceTime" INTEGER NOT NULL
);

INSERT INTO "RemoteVolume_Temp" SELECT "ID", "OperationID", "Name", "Type", "Size", "Hash", "State", "VerificationCount", 0 FROM "RemoteVolume";
DROP TABLE "RemoteVolume";
ALTER TABLE "RemoteVolume_Temp" RENAME TO "RemoteVolume";

UPDATE "Version" SET "Version" = 3;
