-- Downgrade: Remove the Enabled column from the Backup table.
-- A plain DROP COLUMN keeps the table in place, so foreign keys that reference
-- "Backup" (such as the one on "BackupTargetUrl") are left untouched.
ALTER TABLE "Backup" DROP COLUMN "Enabled";
