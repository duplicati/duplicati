CREATE INDEX "DeletedBlockHashSize" ON "DeletedBlock" ("Hash", "Size");
CREATE UNIQUE INDEX "DeletedBlockHashVolumeID" ON "DeletedBlock" ("Hash", "Size", "VolumeID");
UPDATE "Version" SET "Version" = 14;