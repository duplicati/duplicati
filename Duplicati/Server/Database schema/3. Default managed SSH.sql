-- Set existing SSH connections to be unmanaged
INSERT INTO "BackendSetting" ("TaskID", "Name", "Value") SELECT [ID], 'Use Unmanaged SSH', 'True' FROM Task WHERE "Service" LIKE 'ssh';