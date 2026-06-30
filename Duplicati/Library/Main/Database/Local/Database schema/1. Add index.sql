
CREATE UNIQUE INDEX "FilePath" ON "File" ("Path", "BlocksetID", "MetadataID");
CREATE UNIQUE INDEX "BlocksetFullHash" ON "Blockset" ("FullHash", "Length");
CREATE UNIQUE INDEX "BlockHashSize" ON Block ("Hash", "Size");
CREATE UNIQUE INDEX "RemotevolumeName" ON "Remotevolume" ("Name", "State");

CREATE INDEX "BlocksetEntryIds_Forward" ON "BlocksetEntry" ("BlocksetID", "BlockID");
CREATE INDEX "BlocksetEntryIds_Backwards" ON "BlocksetEntry" ("BlockID", "BlocksetID");
CREATE INDEX "MetadatasetBlocksetID" ON "Metadataset" ("BlocksetID");

UPDATE "Version" SET "Version" = 1;
