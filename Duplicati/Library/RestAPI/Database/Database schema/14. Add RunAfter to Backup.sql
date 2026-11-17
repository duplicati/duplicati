-- Add the run-after columns to the Backup table. A backup with a RunAfterBackupID
-- is queued when the backup it refers to has finished a run.
ALTER TABLE "Backup" ADD COLUMN "RunAfterBackupID" INTEGER NULL;
ALTER TABLE "Backup" ADD COLUMN "RunAfterCondition" TEXT NOT NULL DEFAULT 'Success';
