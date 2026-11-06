-- Downgrade: Remove BackupTargetUrl table
DROP INDEX IF EXISTS "IX_BackupTargetUrl_TargetUrlKey";
DROP INDEX IF EXISTS "IX_BackupTargetUrl_BackupID";
DROP TABLE IF EXISTS "BackupTargetUrl";
