/*
Long-term temporary file records
*/
CREATE TABLE "TempFile" (
    "ID" INTEGER PRIMARY KEY,
    "Origin" TEXT NOT NULL, 
    "Path" TEXT NOT NULL, 
    "Timestamp" INTEGER NOT NULL,
    "Expires" INTEGER NOT NULL
);

