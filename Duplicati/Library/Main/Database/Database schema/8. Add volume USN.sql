CREATE TABLE "ChangeJournalData" (
	"ID" INTEGER PRIMARY KEY,
	"FilesetID" INTEGER NOT NULL,		
	"VolumeName" TEXT NOT NULL,			
	"JournalID" INTEGER NOT NULL,		
	"NextUsn" INTEGER NOT NULL, 		
	"ConfigHash" TEXT NOT NULL	
);

UPDATE "Version" SET "Version" = 8;
