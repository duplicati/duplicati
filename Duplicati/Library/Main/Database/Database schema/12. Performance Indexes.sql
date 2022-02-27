CREATE INDEX "nnc_Metadataset" ON Metadataset ("ID","BlocksetID")
CREATE INDEX "nn_FilesetentryFile" on FilesetEntry ("FilesetID","FileID")

-- Line 602 & 603 LocalBackupDatabase.cs 
-- CREATE INDEX "tmpName1" ON "{0}" ("Path"),tmpName1
-- CREATE INDEX "tmpName2" ON "{0}" ("Path"),tmpName2

CREATE INDEX "nn_FileLookup_BlockMeta" ON FileLookup ("BlocksetID", "MetadataID")

CREATE INDEX "nnc_BlocksetEntry" ON "BlocksetEntry" ("Index", "BlocksetID", "BlockID")

UPDATE "Version" SET "Version" = 12;