CREATE INDEX "BlocklistHashBlocksetIDIndex" ON "BlocklistHash" ("BlocksetID", "Index");

UPDATE "Version" SET "Version" = 7;
