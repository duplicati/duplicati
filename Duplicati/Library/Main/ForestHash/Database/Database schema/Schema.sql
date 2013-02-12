/* 
The operation table is a local table 
that is used to record all operations
for later debug inspection, and can be
used to map log messages to an operation
*/
CREATE TABLE "Operation" (
	"ID" INTEGER PRIMARY KEY,
	"Description" TEXT NOT NULL,
	"Timestamp" DATETIME NOT NULL
);

/*
The remote volumes table keeps track
of the state of all known volumes
*/
CREATE TABLE "Remotevolume" (
	"OperationID" INTEGER NOT NULL,
	"Name" TEXT PRIMARY KEY,
	"Type" TEXT NOT NULL,
	"Size" INTEGER NULL,
	"Hash" TEXT NULL,
	"State" TEXT NOT NULL
);

/*
The fileset is a list of files contained
in a single operation, grouped by an 
operationID
*/

/*TODO: There should be a operation/fileset map
so we can reduce the storage used for repeated 
entries of unchanged files */

CREATE TABLE "Fileset" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"Path" TEXT NOT NULL,
	"Scantime" DATETIME NOT NULL,
	"BlocksetID" INTEGER NOT NULL,
	"MetadataID" INTEGER NOT NULL
);

/*
The blocklist hashes are hashes of
fragments of the blocklists
*/
CREATE TABLE "BlocklistHash" (
	"BlocksetID" INTEGER PRIMARY KEY,
	"Index" INTEGER NOT NULL,
	"Hash" TEXT NOT NULL
);


/*
The blockset is a list of blocks

Note that Length is actually redundant,
it can be calculated by 
SUM(Blockset.Size)

The FullHash is the hash of the entire
blob when reconstructed

*/
CREATE TABLE "Blockset" (
	"ID" INTEGER PRIMARY KEY,
	"Length" INTEGER NOT NULL,
	"FullHash" TEXT NOT NULL
);

/*
The elements of a blocklist,
the hash is the block hash
*/
CREATE TABLE "BlocksetEntry" (
	"BlocksetID" INTEGER NOT NULL,
	"Index" INTEGER NOT NULL,
	"BlockID" INTEGER NOT NULL
);

/*
The individual block hashes,
mapped to the containing file
*/
CREATE TABLE "Block" (
	"ID" INTEGER PRIMARY KEY,
    "Hash" TEXT NOT NULL,
	"Size" INTEGER NOT NULL,
	"File" TEXT NOT NULL
);

/*
A metadata set
*/
CREATE TABLE "Metadataset" (
	"ID" INTEGER PRIMARY KEY,
	"BlocksetID" INTEGER NOT NULL
);

/*
Operations performed on the backend
*/
CREATE TABLE "RemoteOperation" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"Timestamp" DATETIME NOT NULL,
	"Operation" TEXT NOT NULL,
	"Path" TEXT NOT NULL,
	"Data" BLOB NULL
);

/*
Logged events
*/
CREATE TABLE "LogData" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"Timestamp" DATETIME NOT NULL,
	"Type" TEXT NOT NULL,
	"Message" TEXT NOT NULL,
	"Exception" TEXT NULL
);

/*
Internal version tracking
*/
CREATE TABLE "Version" (
    "ID" INTEGER PRIMARY KEY,
    "Version" INTEGER NOT NULL
);

INSERT INTO "Version" ("Version") VALUES (0);
