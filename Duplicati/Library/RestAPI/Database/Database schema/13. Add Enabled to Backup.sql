-- Add the Enabled column to the Backup table. The scheduler skips backups that are disabled.
ALTER TABLE "Backup" ADD COLUMN "Enabled" INTEGER NOT NULL DEFAULT 1;
