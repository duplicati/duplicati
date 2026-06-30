CREATE UNIQUE INDEX "FilesetentryIndex" on "FilesetEntry" ("FilesetID", "FileID");

UPDATE "Version" SET "Version" = 4;