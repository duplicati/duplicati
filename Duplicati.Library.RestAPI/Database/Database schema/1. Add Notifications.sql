/*
Notifications not yet acknowledged by the user
*/
CREATE TABLE "Notification" (
    "ID" INTEGER PRIMARY KEY,
    "Type" TEXT NOT NULL,
    "Title" TEXT NOT NULL,
    "Message" TEXT NOT NULL, 
    "Exception" TEXT NOT NULL, 
    "BackupID" TEXT NULL,
    "Action" TEXT NOT NULL,
    "Timestamp" INTEGER NOT NULL
);

