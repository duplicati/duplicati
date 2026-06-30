BEGIN TRANSACTION;

DROP INDEX "FilesetentryIndex";

ALTER TABLE "FilesetEntry"
  RENAME TO "UPGRADE_FilesetEntry";

-- ["WITHOUT ROWID" works with SQLite v3.8.2 (eq System.Data.SQLite v1.0.90.0, rel 2013-12-23) and later]
CREATE TABLE "FilesetEntry" (
	"FilesetID" INTEGER NOT NULL,
	"FileID" INTEGER NOT NULL,
	"Lastmodified" INTEGER NOT NULL,
	CONSTRAINT "FilesetEntry_PK_FilesetIdFileId" PRIMARY KEY ("FilesetID", "FileID")
) {#if sqlite_version >= 3.8.2} WITHOUT ROWID {#endif};

INSERT INTO "FilesetEntry" ("FilesetID", "FileID", "Lastmodified")
     SELECT "FilesetID", "FileID", "Lastmodified" 
	   FROM "UPGRADE_FilesetEntry";

DROP TABLE "UPGRADE_FilesetEntry";

/* Improved reverse lookup for joining Fileset and File table */
CREATE INDEX "FilesetentryFileIdIndex" on "FilesetEntry" ("FileID");

UPDATE "Version" SET "Version" = 6;

COMMIT;

VACUUM;