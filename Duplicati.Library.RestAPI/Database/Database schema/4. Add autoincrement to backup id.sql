ALTER TABLE "backup" RENAME TO "_backup_old";

CREATE TABLE "Backup" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Tags" TEXT NOT NULL,
    "TargetURL" TEXT NOT NULL,
    "DBPath" TEXT NOT NULL
);

INSERT INTO "backup"
  SELECT *
  FROM "_backup_old";

DROP TABLE "_backup_old";
