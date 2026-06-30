CREATE TEMPORARY TABLE "UniqueReassignTable" AS
SELECT "BlockID", "VolumeID"
FROM "DuplicateBlock"
GROUP BY "BlockID", "VolumeID";

DELETE FROM "DuplicateBlock";

INSERT INTO "DuplicateBlock"("BlockID", "VolumeID")
SELECT "BlockID", "VolumeID" FROM "UniqueReassignTable";

DROP TABLE "UniqueReassignTable";

CREATE UNIQUE INDEX IF NOT EXISTS "UniqueBlockVolumeDuplicateBlock"
ON "DuplicateBlock" ("BlockID", "VolumeID");

UPDATE "Version" SET "Version" = 13;