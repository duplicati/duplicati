CREATE TEMPORARY TABLE "RemoteVolumeTemp" AS
SELECT "ID", "OperationID", "Name", "Type", "Size", "Hash", "State", "VerificationCount", "DeleteGraceTime", "ArchiveTime"
FROM "RemoteVolume";

DROP INDEX IF EXISTS "RemotevolumeName";
DROP INDEX IF EXISTS "RemotevolumeNameOnly";
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
	"DeleteGraceTime" INTEGER NOT NULL,
	"ArchiveTime" INTEGER NOT NULL
);

INSERT INTO "Remotevolume"("ID", "OperationID", "Name", "Type", "Size", "Hash", "State", "VerificationCount", "DeleteGraceTime", "ArchiveTime")
SELECT "ID", "OperationID", "Name", "Type", "Size", "Hash", "State", "VerificationCount", "DeleteGraceTime", "ArchiveTime"
FROM "RemoteVolumeTemp";

DROP TABLE "RemoteVolumeTemp";

CREATE UNIQUE INDEX IF NOT EXISTS "RemotevolumeNameOnly" ON "Remotevolume" ("Name");
CREATE UNIQUE INDEX "RemotevolumeName" ON "Remotevolume" ("Name", "State");
