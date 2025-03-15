CREATE INDEX "FileLookupMetadataID" ON "FileLookup" ("MetadataID");
CREATE INDEX "FilesetEntryFilesetID" ON "FilesetEntry" ("FilesetID");

UPDATE "Version" SET "Version" = 13;
