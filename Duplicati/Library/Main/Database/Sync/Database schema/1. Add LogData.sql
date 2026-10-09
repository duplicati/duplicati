/*
The log data table holds the result of each sync run, so the
outcome can be shown in the job log after the run.
*/
CREATE TABLE "LogData" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Timestamp" INTEGER NOT NULL,
    "Type" TEXT NOT NULL,
    "Message" TEXT NOT NULL,
    "Exception" TEXT NULL
);

CREATE INDEX "LogDataTimestamp" ON "LogData" ("Timestamp");

UPDATE "Version" SET "Version" = 1;
