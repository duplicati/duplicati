CREATE TABLE "PathEntry" (
	"ID" INTEGER PRIMARY KEY,
	"PathKey" INTEGER NOT NULL,
	"PathEntryID" INTEGER NOT NULL,
	"FilenameX" TEXT NOT NULL
);

CREATE TABLE "File_Temp" (
	"ID" INTEGER PRIMARY KEY,
	"PathEntryID" INTEGER NOT NULL,
	"BlocksetID" INTEGER NOT NULL,
	"MetadataID" INTEGER NOT NULL
);

INSERT INTO "PathEntry" ("PathKey", "PathEntryID", "FilenameX") SELECT 0, -1, "Path" FROM "File";
INSERT INTO "File_Temp" ("ID", "PathEntryID", "BlocksetID", "MetadataID") SELECT "A"."ID", "B"."ID", "A"."BlocksetID", "A"."MetadataID" FROM "Path" "A", "PathEntry" "B" WHERE "A"."Path" = "B"."FilenameX";

DROP INDEX "FilePath";
DROP TABLE "Path";

ALTER TABLE "File_Temp" RENAME TO "File";

CREATE UNIQUE INDEX "FilePath" ON "PathEntry" ("PathEntryID", "BlocksetID", "MetadataID");
CREATE UNIQUE INDEX "PathEntryLookup" ON "PathEntry" ("FilenameX", "PathEntryID");
CREATE UNIQUE INDEX "PathEntryFilename" ON "PathEntry" ("FilenameX");
CREATE INDEX "PathEntryKey" ON "File" ("PathKey");

CREATE VIEW "FullPathEntry" AS SELECT "X0"."ID", CASE WHEN "X1"."FilenameX" IS NULL THEN "" ELSE "X1"."FilenameX" END CASE || "X0"."FilenameX" AS "PathX" FROM "PathEntry" "X0" LEFT OUTER JOIN "PathEntry" "X1" ON "X0"."PathEntryID" = "X1"."ID";

UPDATE "Version" SET "Version" = 4;
