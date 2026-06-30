-- Add the OperationType column to the Backup table.
ALTER TABLE "Backup" ADD COLUMN "OperationType" TEXT NOT NULL DEFAULT 'Backup';
