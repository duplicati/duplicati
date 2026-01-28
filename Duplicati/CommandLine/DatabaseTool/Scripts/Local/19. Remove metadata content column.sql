CREATE TEMPORARY TABLE "MetadatasetTemp" AS
SELECT "ID", "BlocksetID"
FROM "Metadataset";

DROP TABLE "Metadataset";
CREATE TABLE "Metadataset" (
    "ID" INTEGER PRIMARY KEY,
    "BlocksetID" INTEGER NOT NULL
);
INSERT INTO "Metadataset"("ID", "BlocksetID")
SELECT "ID", "BlocksetID"
FROM "MetadatasetTemp";

DROP TABLE "MetadatasetTemp";

CREATE INDEX "MetadatasetBlocksetID" ON "Metadataset" ("BlocksetID");
CREATE INDEX "nnc_Metadataset" ON Metadataset ("ID","BlocksetID");