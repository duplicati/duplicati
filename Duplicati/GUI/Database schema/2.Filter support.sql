CREATE TABLE "TaskFilter" (
    "ID" INTEGER PRIMARY KEY,
    "SortOrder" INTEGER NULL,
    "Include" BOOLEAN NULL,
    "Filter" TEXT NULL,
    "TaskID" INTEGER NULL
);
