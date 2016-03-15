
DROP INDEX "BlocksetEntryIds_Forward";
DROP INDEX "BlocksetEntryIds_Backwards";

ALTER TABLE "BlocksetEntry"
  RENAME TO "UPGRADE_BlocksetEntry";

-- ["WITHOUT ROWID" works with SQLite v3.8.2 (eq System.Data.SQLite v1.0.90.0, rel 2013-12-23) and later]
CREATE TABLE "BlocksetEntry" (
	"BlocksetID" INTEGER NOT NULL,
	"Index" INTEGER NOT NULL,
	"BlockID" INTEGER NOT NULL,
	CONSTRAINT "BlocksetEntry_PK_IdIndex" PRIMARY KEY ("BlocksetID", "Index")
) {#if sqlite_version >= 3.8.2} WITHOUT ROWID {#endif};

INSERT INTO "BlocksetEntry" ("BlocksetID", "Index", "BlockID")
     SELECT "BlocksetID", "Index", "BlockID" 
	   FROM "UPGRADE_BlocksetEntry";

DROP TABLE "UPGRADE_BlocksetEntry";

/* As this table is a cross table we need fast lookup */
CREATE INDEX "BlocksetEntry_IndexIdsBackwards" ON "BlocksetEntry" ("BlockID");


/* Add index for faster volume based block access (for compacting) */
CREATE INDEX "Block_IndexByVolumeId" ON "Block" ("VolumeID");


UPDATE "Version" SET "Version" = 5;