-- Create the BackupTargetUrl table for storing multiple target URLs per backup
CREATE TABLE "BackupTargetUrl" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "BackupID" INTEGER NOT NULL,
    "TargetUrlKey" TEXT NOT NULL,
    "TargetURL" TEXT NOT NULL,
    "Mode" TEXT NOT NULL DEFAULT 'inline',
    "Interval" TEXT NULL,
    "Options" TEXT NULL,
    "CreatedAt" INTEGER NOT NULL DEFAULT 0,
    "UpdatedAt" INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY ("BackupID") REFERENCES "Backup"("ID") ON DELETE CASCADE
);

CREATE INDEX "IX_BackupTargetUrl_BackupID" ON "BackupTargetUrl" ("BackupID");
CREATE UNIQUE INDEX "IX_BackupTargetUrl_TargetUrlKey" ON "BackupTargetUrl" ("TargetUrlKey");
