
CREATE TABLE "FilesetEntry_Temp" (
	"FilesetID" INTEGER NOT NULL,
	"FileID" INTEGER NOT NULL,
	"Lastmodified" INTEGER NOT NULL
);

INSERT INTO "FilesetEntry_Temp" SELECT "FilesetID", "FileID", 0 FROM "FilesetEntry";
DROP TABLE "FilesetEntry";
ALTER TABLE "FilesetEntry_Temp" RENAME TO "FilesetEntry";

UPDATE "Version" SET "Version" = 2;

