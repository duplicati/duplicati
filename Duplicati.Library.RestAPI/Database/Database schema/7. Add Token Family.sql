/*
Token Family table
*/
CREATE TABLE "TokenFamily" (
    "ID" TEXT PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "Counter" INTEGER NOT NULL,
    "LastUpdated" INTEGER NOT NULL
);