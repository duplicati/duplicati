CREATE INDEX IF NOT EXISTS "BlockSize" ON "Block" ("Size");
CREATE UNIQUE INDEX IF NOT EXISTS "BlockHashVolumeID" ON "Block" ("Hash", "VolumeID");

UPDATE "Version" SET "Version" = 11;
