CREATE TABLE "ConnectionString" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL UNIQUE,
    "Description" TEXT NOT NULL DEFAULT '',
    "BaseUrl" TEXT NOT NULL,
    "CreatedAt" INTEGER NOT NULL,
    "UpdatedAt" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_ConnectionString_Name" ON "ConnectionString" ("Name");

ALTER TABLE "Backup" ADD COLUMN "ConnectionStringID" INTEGER NOT NULL DEFAULT -1;
