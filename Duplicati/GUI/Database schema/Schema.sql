CREATE TABLE "Task" (
    "ID" INTEGER PRIMARY KEY,
    "Service" TEXT NULL,
    "Encryptionkey" TEXT NULL,
    "Signaturekey" TEXT NULL,
    "SourcePath" TEXT NULL,
    "ScheduleID" INTEGER NULL,
    "KeepFull" INTEGER NULL,
    "KeepTime" TEXT NULL,
    "MaxUploadsize" TEXT NULL,
    "UploadBandwidth" TEXT NULL,
    "DownloadBandwidth" TEXT NULL,
    "VolumeSize" TEXT NULL,
	"FullAfter" TEXT NULL,
	"ThreadPriority" TEXT NULL,
	"AsyncTransfer" BOOLEAN NULL,
	"GPGEncryption" BOOLEAN NULL,
	"IncludeSetup" BOOLEAN NULL,
	"IgnoreTimestamps" BOOLEAN NULL
);

CREATE TABLE "Schedule" (
    "ID" INTEGER PRIMARY KEY,
    "Name" TEXT NULL,
    "Path" TEXT NULL,
    "When" DATETIME NULL,
    "Repeat" TEXT NULL,
    "Weekdays" TEXT NULL
);


CREATE TABLE "TaskSetting" (
    "ID" INTEGER PRIMARY KEY,
    "TaskID" INTEGER NULL,
    "Name" TEXT NULL,
    "Value" TEXT NULL
);

CREATE TABLE "CommandQueue" (
    "ID" INTEGER PRIMARY KEY,
    "Command" TEXT NULL,
    "Argument" TEXT NULL,
    "Completed" BOOLEAN NULL
);

CREATE TABLE "Log" (
    "ID" INTEGER PRIMARY KEY,
    "TaskID" INTEGER NULL,
    "EndTime" DATETIME NULL,
    "BeginTime" DATETIME NULL,
    "Action" TEXT NULL,
    "SubAction" TEXT NULL,
    "Transfersize" INTEGER NULL,
    "ParsedStatus" TEXT NULL,
    "LogBlobID" INTEGER NULL
);

CREATE TABLE "LogBlob" (
    "ID" INTEGER PRIMARY KEY,
    "Data" BLOB NULL
);

CREATE TABLE "Version" (
    "ID" INTEGER PRIMARY KEY,
    "Version" INTEGER
);

CREATE TABLE "ApplicationSetting" (
    "ID" INTEGER PRIMARY KEY,
    "Name" TEXT NULL,
    "Value" TEXT NULL
);

CREATE TABLE "TaskFilter" (
    "ID" INTEGER PRIMARY KEY,
    "SortOrder" INTEGER NULL,
    "Include" BOOLEAN NULL,
    "Filter" TEXT NULL,
    "TaskID" INTEGER NULL
);


INSERT INTO "Version" ("Version") VALUES (1);