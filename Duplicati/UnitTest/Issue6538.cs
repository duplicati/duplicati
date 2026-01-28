using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class Issue6538 : BasicSetupHelper
    {
        private class LogEntry
        {
            public string Type { get; set; }
            public string Message { get; set; }
        }

        [Test]
        [Category("Issue6538")]
        public void JobLogShouldBeWrittenOnError()
        {
            // Arrange
            var testopts = TestOptions;
            testopts["backup-test-samples"] = "0";

            // 1. Run a successful backup first to ensure the database is created
            var sourceFolder = Path.Combine(DATAFOLDER, "source");
            Directory.CreateDirectory(sourceFolder);
            File.WriteAllText(Path.Combine(sourceFolder, "file.txt"), "hello");

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var res = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, res.Errors.Count());
            }

            // 2. Create a backup with an invalid source path to trigger an error
            var invalidSourcePath = Path.Combine(DATAFOLDER, "nonexistent_folder_" + Guid.NewGuid().ToString());

            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            {
                // Act - This should fail because the source doesn't exist
                IBackupResults backupResults = null;
                try
                {
                    backupResults = c.Backup(new[] { invalidSourcePath });
                }
                catch
                {
                    // Expected to throw
                }

                // Assert - Check that the job log was written to the database
                using (var connection = new SqliteConnection($"Data Source={testopts["dbpath"]};Pooling=false"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    // We want to check the LAST log entry, or ensure there is a NEW one.
                    // Since we ran a backup before, there will be logs.
                    // We specifically look for an Error/Fatal result.
                    command.CommandText = "SELECT Type, Message FROM LogData ORDER BY Timestamp DESC LIMIT 10";

                    var logEntries = new List<LogEntry>();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var entry = new LogEntry
                            {
                                Type = reader.GetString(0),
                                Message = reader.GetString(1)
                            };
                            logEntries.Add(entry);
                        }
                    }

                    // The log should contain a Result entry that indicates an error
                    // We check for "ParsedResult":"Error" or "ParsedResult":"Fatal" to avoid matching the successful backup
                    // which contains keys like "ReportedQuotaError" or "FilesWithError"
                    var resultEntry = logEntries.FirstOrDefault(e =>
                        e.Type == "Result" &&
                        (e.Message.Contains("\"ParsedResult\":\"Error\"") || e.Message.Contains("\"ParsedResult\":\"Fatal\""))
                    );

                    Assert.IsNotNull(resultEntry, "Result entry with Error/Fatal should be present in job log");
                }
            }
        }
    }
}
