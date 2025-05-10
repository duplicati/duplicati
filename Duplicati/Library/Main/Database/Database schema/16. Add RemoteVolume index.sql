CREATE UNIQUE INDEX IF NOT EXISTS "RemotevolumeNameOnly" ON "Remotevolume" ("Name");
CREATE INDEX IF NOT EXISTS "FileLookupMetadataID" ON "FileLookup" ("MetadataID");
UPDATE "Version" SET "Version" = 16;