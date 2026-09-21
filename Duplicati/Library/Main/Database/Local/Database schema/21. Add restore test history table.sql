CREATE TABLE "RestoreTestHistory" (
	"ID" INTEGER PRIMARY KEY,
	"Path" TEXT NOT NULL,
	"LastVerified" INTEGER NOT NULL,
	"LastResult" TEXT NOT NULL
);
CREATE UNIQUE INDEX "RestoreTestHistoryPath" ON "RestoreTestHistory" ("Path");
