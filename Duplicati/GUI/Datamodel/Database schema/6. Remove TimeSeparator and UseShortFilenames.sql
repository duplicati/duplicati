-- Remove unused options
DELETE FROM TaskExtension WHERE [Name] LIKE 'File Time Seperator';
DELETE FROM TaskExtension WHERE [Name] LIKE 'Short Filenames';

-- Remove any empty filename prefixes which should be most common
DELETE FROM TaskExtension WHERE [Name] LIKE 'Filename Prefix' AND ([Value] LIKE '' OR [Value] IS NULL);

-- Move any non-empty filename prefixes into the manual settings override
INSERT INTO TaskOverride ([TaskID], [Name], [Value]) SELECT [TaskID], 'filename-prefix', [Value] FROM TaskExtension WHERE [Name] LIKE 'Filename Prefix';