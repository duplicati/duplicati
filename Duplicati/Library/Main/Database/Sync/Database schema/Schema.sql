/*
The remote inventory table is a local cache of what a remote
listing operation returned. It records the observed state of
files on the remote destination and is used as the diff baseline
against the local source. It is refreshed from a remote listing
and is NOT updated as part of recording operation intent.
*/
CREATE TABLE "RemoteInventory" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "RelativePath" TEXT UNIQUE NOT NULL,
    "Size" INTEGER NOT NULL,
    "LastModified" DATETIME NOT NULL,
    "ContentHash" TEXT NULL,
    "LastVerified" DATETIME NOT NULL
);

/*
The pending operation table is a mutable journal of in-flight
intent. A row is inserted BEFORE an operation (upload/update/
delete) is performed on the remote destination, and removed when
the operation either completes or is reconciled on resume. It is
never purged by time - its sole purpose is to make a crash or
other incident recoverable: on the next run, leftover rows tell
the program exactly what was being attempted so it can reconcile
against a fresh remote listing rather than silently dropping state.
*/
CREATE TABLE "PendingOperation" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Path" TEXT UNIQUE NOT NULL,
    "Operation" TEXT NOT NULL,
    "Size" INTEGER,
    "Hash" TEXT,
    "StartedAt" INTEGER NOT NULL,
    "Attempts" INTEGER NOT NULL DEFAULT 0
);

/*
The remote operation table is an append-only audit log of
completed backend calls, used for later debug inspection. It is
distinct from PendingOperation: RemoteOperation records what
happened, PendingOperation records what is about to happen.
Entries older than 30 days are purged automatically.
*/
CREATE TABLE "RemoteOperation" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Timestamp" INTEGER NOT NULL,
    "Operation" TEXT NOT NULL,
    "Path" TEXT NOT NULL,
    "Data" TEXT NULL
);

-- Index supporting the time-based purge of old audit-log rows.
CREATE INDEX "RemoteOperationTimestamp" ON "RemoteOperation" ("Timestamp");

CREATE TABLE "Version" (
    "ID" INTEGER PRIMARY KEY,
    "Version" INTEGER NOT NULL
);

INSERT INTO "Version" ("Version") VALUES (0);
