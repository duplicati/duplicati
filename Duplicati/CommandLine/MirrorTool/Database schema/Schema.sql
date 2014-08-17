/*
 * The primary table that stores all files.
 */
CREATE TABLE "File" (
    "Path" TEXT PRIMARY KEY NOT NULL,
    "Local" INTEGER NOT NULL,
    "Remote" INTEGER NOT NULL
);

/*
 * The log of errors
 */
CREATE TABLE "Log" (
    "ID" INTEGER PRIMARY KEY,
    "Message" TEXT NOT NULL,
    "Exception" TEXT,
    "Timestamp" INTEGER NOT NULL
);

/*
Internal version tracking
*/
CREATE TABLE "Version" (
    "ID" INTEGER PRIMARY KEY,
    "Version" INTEGER NOT NULL
);

INSERT INTO "Version" ("Version") VALUES (0);

