CREATE TEMPORARY TABLE "RemoteVolumeTemp" AS
SELECT "ID", "OperationID", "Name", "Type", "Size", "Hash", "State", "VerificationCount", "DeleteGraceTime"
FROM "RemoteVolume";

DROP INDEX IF EXISTS "RemotevolumeName";
DROP TABLE "RemoteVolume";

CREATE TABLE "Remotevolume" (
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

INSERT INTO "Remotevolume"("ID", "OperationID", "Name", "Type", "Size", "Hash", "State", "VerificationCount", "DeleteGraceTime")
SELECT "ID", "OperationID", "Name", "Type", "Size", "Hash", "State", "VerificationCount", "DeleteGraceTime"
FROM "RemoteVolumeTemp";

DROP TABLE "RemoteVolumeTemp";

CREATE UNIQUE INDEX "RemotevolumeName" ON "Remotevolume" ("Name", "State");
