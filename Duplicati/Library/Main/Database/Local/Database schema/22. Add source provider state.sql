CREATE TABLE "SourceProviderState" (
    "ID" INTEGER PRIMARY KEY,
    "FilesetID" INTEGER NOT NULL,
    "Scope" TEXT NOT NULL,
    "Key" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "ConfigHash" TEXT NOT NULL
);
CREATE INDEX "SourceProviderStateFilesetID" ON "SourceProviderState" ("FilesetID");
