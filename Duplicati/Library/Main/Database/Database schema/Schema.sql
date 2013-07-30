/* 
The operation table is a local table 
that is used to record all operations
for later debug inspection, and can be
used to map log messages to an operation
*/
CREATE TABLE "Operation" (
	"ID" INTEGER PRIMARY KEY,
	"Description" TEXT NOT NULL,
	"Timestamp" INTEGER NOT NULL
);

/*
The remote volumes table keeps track
of the state of all known volumes
*/
CREATE TABLE "Remotevolume" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"Name" TEXT NOT NULL,
	"Type" TEXT NOT NULL,
	"Size" INTEGER NULL,
	"Hash" TEXT NULL,
	"State" TEXT NOT NULL,
	"VerificationCount" INTEGER NOT NULL
);

/*
The index-block table contains
references that explains what block
files a index file references.
This is used to remove index volumes,
when they no longer reference any
block volumes	
*/
CREATE TABLE "IndexBlockLink" (
	"IndexVolumeID" INTEGER NOT NULL,
	"BlockVolumeID" INTEGER NOT NULL
);

/*
The fileset collects all files belonging to 
a particular backup, and thus a remote Fileset
*/
CREATE TABLE "Fileset" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"VolumeID" INTEGER NOT NULL,
	"Timestamp" INTEGER NOT NULL
);

/*
The OperationFileset contains an
entry for each file scanned for
a single operation. The scantime
is the time the file was last 
scanned in UNIX EPOCH format
*/
CREATE TABLE "FilesetEntry" (
	"FilesetID" INTEGER NOT NULL,
	"FileID" INTEGER NOT NULL,
	"Scantime" INTEGER NOT NULL
);

/*
The FileEntry contains an ID
for each path and each version
of the data and metadata
*/
CREATE TABLE "File" (
	"ID" INTEGER PRIMARY KEY,
	"Path" TEXT NOT NULL,
	"BlocksetID" INTEGER NOT NULL,
	"MetadataID" INTEGER NOT NULL
);

/*
The blocklist hashes are hashes of
fragments of the blocklists.
They are grouped by the BlocksetID
and ordered by the index
*/
CREATE TABLE "BlocklistHash" (
	"BlocksetID" INTEGER NOT NULL,
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
the hash is the block hash,
they are grouped by the BlocksetID
and ordered by the index
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
	"VolumeID" INTEGER NOT NULL
);

/*
The deleted block hashes,
mapped to the containing file,
used for wasted space computations
*/
CREATE TABLE "DeletedBlock" (
	"ID" INTEGER PRIMARY KEY,
    "Hash" TEXT NOT NULL,
	"Size" INTEGER NOT NULL,
	"VolumeID" INTEGER NOT NULL
);

/*
If extra copies of blocks are detected, 
they are recorded here
*/
CREATE TABLE "DuplicateBlock" (
    "BlockID" INTEGER NOT NULL,
    "VolumeID" INTEGER NOT NULL
);


/*
A metadata set, essentially a placeholder
to easily extend metadatasets with new properties
*/
CREATE TABLE "Metadataset" (
	"ID" INTEGER PRIMARY KEY,
	"BlocksetID" INTEGER NOT NULL
);

/*
Operations performed on the backend,
intended to be used when constructing
an error report or when debugging
*/
CREATE TABLE "RemoteOperation" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"Timestamp" INTEGER NOT NULL,
	"Operation" TEXT NOT NULL,
	"Path" TEXT NOT NULL,
	"Data" BLOB NULL
);

/*
Logged events, intended to be used when 
constructing an error report or when 
debugging
*/
CREATE TABLE "LogData" (
	"ID" INTEGER PRIMARY KEY,
	"OperationID" INTEGER NOT NULL,
	"Timestamp" INTEGER NOT NULL,
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

/*
Settings, such as hash and blocksize,
used for verification
*/
CREATE TABLE "Configuration" (
	"Key" TEXT PRIMARY KEY NOT NULL,
	"Value" TEXT NOT NULL
);

INSERT INTO "Version" ("Version") VALUES (0);
