CREATE TABLE "Remotevolume_Temp" (
  "ID" INTEGER PRIMARY KEY,
  "OperationID" INTEGER NOT NULL,
  "Name" TEXT NOT NULL,
  "Type" TEXT NOT NULL,
  "Size" INTEGER NULL,
  "Hash" TEXT NULL,
  "State" TEXT NOT NULL,
  "VerificationCount" INTEGER NOT NULL,
  "DeleteGraceTime" INTEGER NOT NULL,
  "ArchiveTime" INTEGER NOT NULL,
  "LockExpirationTime" INTEGER NOT NULL
);

INSERT INTO "Remotevolume_Temp" (
  "ID","OperationID","Name","Type","Size","Hash","State",
  "VerificationCount","DeleteGraceTime","ArchiveTime","LockExpirationTime"
)
SELECT
  "ID","OperationID","Name","Type","Size","Hash","State",
  "VerificationCount","DeleteGraceTime","ArchiveTime", 0
FROM "Remotevolume";

DROP TABLE "Remotevolume";
ALTER TABLE "Remotevolume_Temp" RENAME TO "Remotevolume";

CREATE UNIQUE INDEX IF NOT EXISTS "RemotevolumeNameOnly" ON "Remotevolume" ("Name");
CREATE UNIQUE INDEX "RemotevolumeName" ON "Remotevolume" ("Name", "State");