/*
The PathPrefix contains a set
of path prefixes, used to minimize
the space required to store paths
*/
CREATE TABLE "PathPrefix" (
    "ID" INTEGER PRIMARY KEY,
    "Prefix" TEXT NOT NULL
);
CREATE UNIQUE INDEX "PathPrefixPrefix" ON "PathPrefix" ("Prefix");


/*
The FileLookup table contains an ID
for each path and each version
of the data and metadata
*/
CREATE TABLE "FileLookup" (
    "ID" INTEGER PRIMARY KEY,
    "PrefixID" INTEGER NOT NULL,
    "Path" TEXT NOT NULL,
    "BlocksetID" INTEGER NOT NULL,
    "MetadataID" INTEGER NOT NULL
);

/* Fast path based lookup, single properties are auto-indexed */
CREATE UNIQUE INDEX "FileLookupPath" ON "FileLookup" ("PrefixID", "Path", "BlocksetID", "MetadataID");


/* Build the prefix table */
INSERT INTO "PathPrefix" ("Prefix")
SELECT DISTINCT
    CASE SUBSTR("Path", LENGTH("Path")) WHEN  '/' THEN
        rtrim(SUBSTR("Path", 1, LENGTH("Path")-1), replace(replace(SUBSTR("Path", 1, LENGTH("Path")-1), "\", "/"), '/', ''))
    ELSE
        rtrim("Path", replace(replace("Path", "\", "/"), '/', ''))
    END AS "Prefix"
FROM "File";

/* Build the path lookup table */
INSERT INTO "FileLookup" ("Path", "PrefixID", "BlocksetID", "MetadataID")

SELECT 
  SUBSTR("Path", LENGTH("ParentFolder") + 1) AS "Path", 
  "ID" AS "PrefixID", 
  "BlocksetID", 
  "MetadataID" 
FROM

(SELECT "Path", "BlocksetID", "MetadataID",
    CASE SUBSTR("Path", LENGTH("Path")) WHEN  '/' THEN
        rtrim(SUBSTR("Path", 1, LENGTH("Path")-1), replace(replace(SUBSTR("Path", 1, LENGTH("Path")-1), "\", "/"), '/', ''))
    ELSE
        rtrim("Path", replace(replace("Path", "\", "/"), '/', ''))
    END AS "ParentFolder"
FROM "File") "A" INNER JOIN "PathPrefix" "B" ON "A"."ParentFolder" = "B"."Prefix";

DROP TABLE "File";

CREATE VIEW "File" AS SELECT "A"."ID" AS "ID", "B"."Prefix" || "A"."Path" AS "Path", "A"."BlocksetID" AS "BlocksetID", "A"."MetadataID" AS "MetadataID" FROM "FileLookup" "A", "PathPrefix" "B" WHERE "A"."PrefixID" = "B"."ID";


UPDATE "Version" SET "Version" = 9;