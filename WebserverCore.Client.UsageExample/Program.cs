// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using Duplicati.WebserverCore.Client;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Dto.V2;

class Program
{
    static async Task Main(string[] args)
    {
        var serverUrl = "http://localhost:8200";

        var password = Environment.GetEnvironmentVariable("TEST_PASSWORD") ??
                      throw new InvalidOperationException("Must set the TEST_PASSWORD environment variable");

        using var client = new DuplicatiServerClient(serverUrl, ServerCredentialType.Password, password);

        try
        {
            // Authenticate explicitly (optional - happens automatically on first API call)
            await client.Authenticate();
            Console.WriteLine("✓ Authentication successful");

            // Demonstrate all client methods
            await DemonstrateAuthenticationMethods(client);
            await DemonstrateBackupManagement(client);
            await DemonstrateBackupOperations(client);
            await DemonstrateBackupDataAccess(client);
            await DemonstrateDatabaseManagement(client);
            await DemonstrateExportOperations(client);
            await DemonstrateServerManagement(client);
            await DemonstrateTaskManagement(client);
            await DemonstrateSystemInformation(client);
            await DemonstrateSettingsManagement(client);
            await DemonstrateFilesystemOperations(client);
            await DemonstrateV2ApiMethods(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates authentication-related methods
    /// </summary>
    static async Task DemonstrateAuthenticationMethods(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Authentication Methods ===");

        try
        {
            // Issue a signin token
            var signinTokenResult = await client.IssueSigninTokenV1Async(
                new IssueSigninTokenInputDto(Environment.GetEnvironmentVariable("TEST_PASSWORD")!) { }, CancellationToken.None);
            Console.WriteLine($"✓ Signin token issued: {signinTokenResult.Token[..10]}...");

            // Issue a single operation token
            var operationToken = await client.IssueTokenV1Async("export", CancellationToken.None);
            Console.WriteLine($"✓ Operation token issued: {operationToken.Token[..10]}...");

            await client.Authenticate();
            // Issue a forever token
            try
            {
                var foreverToken = await client.IssueForeverTokenV1Async(CancellationToken.None);
                Console.WriteLine($"✓ Forever token issued: {foreverToken.Token[..10]}...");
            }
            catch (Exception)
            {
                // expected here if foreever tokens are disabled
            }

            // Refresh token (if using refresh tokens)
            try
            {
                var refreshResult = await client.RefreshTokenV1Async(null, CancellationToken.None);
                Console.WriteLine($"✓ Token refreshed: {refreshResult.AccessToken[..10]}...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ℹ️ Token refresh not available: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Authentication demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates backup management methods
    /// </summary>
    static async Task DemonstrateBackupManagement(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Backup Management ===");

        try
        {
            // List all backups
            var backups = await client.ListBackupsV1Async(CancellationToken.None);
            Console.WriteLine($"✓ Found {backups.Length} backups");

            foreach (var backup in backups)
            {
                Console.WriteLine($"  - {backup.Backup.Name} (ID: {backup.Backup.ID})");

                // Get detailed backup information
                var backupDetails = await client.GetBackupV1Async(backup.Backup.ID, CancellationToken.None);
                Console.WriteLine($"    Name: {backupDetails.Backup.Name}");
                Console.WriteLine($"    Sources: {string.Join(", ", backupDetails.Backup.Sources ?? [])}");


                var exportToken = await client.IssueTokenV1Async("export", CancellationToken.None);
                // Export backup configuration
                var exportedBackup = await client.ExportBackupV1Async(backup.Backup.ID, true, "random", exportToken.Token, CancellationToken.None);
                Console.WriteLine($"    ✓ Backup configuration exported the stream is {exportedBackup.Length} bytes long");

                // Export as command line
                var cmdlineExport = await client.ExportCommandlineV1Async(backup.Backup.ID, CancellationToken.None);
                Console.WriteLine($"    ✓ Command line exported: {cmdlineExport!.Command![..50]}...");

                // Export arguments only
                var argsExport = await client.ExportArgsOnlyV1Async(backup.Backup.ID, CancellationToken.None);
                Console.WriteLine($"    ✓ Arguments exported: {argsExport.Arguments.ToArray().Length} args");
            }

            // Demonstrate backup creation (commented out to avoid creating test backups)
            /*
            var newBackup = new BackupDto
            {
                Name = "Test Backup",
                Description = "Created via API example",
                TargetURL = "file:///tmp/test-backup",
                Sources = ["~/Documents"],
                Settings = new Dictionary<string, string>
                {
                    { "compression-module", "zip" },
                    { "encryption-module", "aes" }
                }
            };

            var createdBackup = await client.CreateBackupV1Async(newBackup, CancellationToken.None);
            Console.WriteLine($"✓ Created backup: {createdBackup.Name}");

            // Update the backup
            createdBackup.Description = "Updated via API";
            var updatedBackup = await client.UpdateBackupV1Async(createdBackup.ID, createdBackup, CancellationToken.None);
            Console.WriteLine($"✓ Updated backup: {updatedBackup.Description}");

            // Delete the backup
            var deleteResult = await client.DeleteBackupV1Async(createdBackup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Deleted backup: {deleteResult.DeletedFileCount} files deleted");
            */
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Backup management demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates backup operations
    /// </summary>
    static async Task DemonstrateBackupOperations(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Backup Operations ===");

        try
        {
            var backups = await client.ListBackupsV1Async(CancellationToken.None);
            if (backups.Length == 0)
            {
                Console.WriteLine("ℹ️ No backups found to demonstrate operations");
                return;
            }

            var firstBackup = backups[0];
            Console.WriteLine($"Demonstrating operations on backup: {firstBackup.Backup.Name}");

            // Start a backup (commented out to avoid actual backup operations)
            /*
            var startResult = await client.StartBackupV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Backup started: Task ID {startResult.TaskID}");

            // Run a backup
            var runResult = await client.RunBackupV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Backup run: Task ID {runResult.TaskID}");

            // Verify backup
            var verifyResult = await client.VerifyBackupV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Backup verification started: Task ID {verifyResult.TaskID}");

            // Compact backup
            var compactResult = await client.CompactBackupV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Backup compaction started: Task ID {compactResult.TaskID}");

            // Vacuum backup database
            var vacuumResult = await client.VacuumBackupV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Database vacuum started: Task ID {vacuumResult.TaskID}");

            // Repair backup
            var repairInput = new RepairInputDto { OnlyLogErrors = true };
            var repairResult = await client.RepairBackupV1Async(firstBackup.Backup.ID, repairInput, CancellationToken.None);
            Console.WriteLine($"✓ Backup repair started: Task ID {repairResult.TaskID}");

            // Repair and update backup
            var repairUpdateResult = await client.RepairUpdateBackupV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Backup repair/update started: Task ID {repairUpdateResult.TaskID}");

            // Restore files
            var restoreInput = new RestoreInputDto
            {
                Path = "~/Documents/test.txt",
                RestoreLocation = "~/Desktop/restored_test.txt"
            };
            var restoreResult = await client.RestoreBackupV1Async(firstBackup.Backup.ID, restoreInput, CancellationToken.None);
            Console.WriteLine($"✓ File restoration started: Task ID {restoreResult.TaskID}");
            */

            Console.WriteLine("ℹ️ Backup operations are commented out to avoid actual operations");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Backup operations demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates backup data access methods
    /// </summary>
    static async Task DemonstrateBackupDataAccess(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Backup Data Access ===");

        try
        {
            var backups = await client.ListBackupsV1Async(CancellationToken.None);
            if (backups.Length == 0)
            {
                Console.WriteLine("ℹ️ No backups found to demonstrate data access");
                return;
            }

            var firstBackup = backups[0];
            Console.WriteLine($"Accessing data for backup: {firstBackup.Backup.Name}");

            // List files in backup
            var files = await client.ListFilesV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Found {files.Length} files/folders in backup");

            // List filesets
            var filesets = await client.ListFilesetsV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Found {filesets.Length} filesets");

            // Get backup log
            var backupLog = await client.GetBackupLogV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Retrieved backup log: {backupLog.Length} entries");

            // Get remote log
            var remoteLog = await client.GetRemoteLogV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Retrieved remote log: {remoteLog.Length} entries");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Backup data access demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates database management methods
    /// </summary>
    static async Task DemonstrateDatabaseManagement(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Database Management ===");

        try
        {
            var backups = await client.ListBackupsV1Async(CancellationToken.None);
            if (backups.Length == 0)
            {
                Console.WriteLine("ℹ️ No backups found to demonstrate database management");
                return;
            }

            var firstBackup = backups[0];
            Console.WriteLine($"Database management for backup: {firstBackup.Backup.Name}");

            // Database operations are commented out to avoid destructive actions
            /*
            // Move database
            var moveDbInput = new UpdateDbPathInputDto { Path = "/tmp/new_db_location" };
            var moveResult = await client.MoveDatabaseV1Async(firstBackup.Backup.ID, moveDbInput, CancellationToken.None);
            Console.WriteLine($"✓ Database move started: Task ID {moveResult.TaskID}");

            // Update database path
            var updateDbInput = new UpdateDbPathInputDto { Path = "/tmp/updated_db_location" };
            var updateResult = await client.UpdateDatabaseV1Async(firstBackup.Backup.ID, updateDbInput, CancellationToken.None);
            Console.WriteLine($"✓ Database update started: Task ID {updateResult.TaskID}");

            // Delete database
            var deleteDbResult = await client.DeleteDatabaseV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Database deletion started: Task ID {deleteDbResult.TaskID}");
            */

            Console.WriteLine("ℹ️ Database operations are commented out to avoid destructive actions");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Database management demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates export operations
    /// </summary>
    static async Task DemonstrateExportOperations(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Export Operations ===");

        try
        {
            var backups = await client.ListBackupsV1Async(CancellationToken.None);
            if (backups.Length == 0)
            {
                Console.WriteLine("ℹ️ No backups found to demonstrate export operations");
                return;
            }

            var firstBackup = backups[0];
            Console.WriteLine($"Export operations for backup: {firstBackup.Backup.Name}");

            // Export backup configuration
            var exportToken = await client.IssueTokenV1Async("export", CancellationToken.None);
            var exportedConfig = await client.ExportBackupV1Async(firstBackup.Backup.ID, true, "random", exportToken.Token, CancellationToken.None);
            Console.WriteLine($"✓ Exported backup configuration: {exportedConfig}");

            // Export as command line
            var cmdlineExport = await client.ExportCommandlineV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Exported command line: {cmdlineExport!.Command![..50]}...");

            // Export arguments only
            var argsExport = await client.ExportArgsOnlyV1Async(firstBackup.Backup.ID, CancellationToken.None);
            Console.WriteLine($"✓ Exported arguments: {argsExport.Arguments.ToArray().Length} arguments");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Export operations demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates server management methods
    /// </summary>
    static async Task DemonstrateServerManagement(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Server Management ===");

        try
        {
            // Get server state
            var serverState = await client.GetServerStateV1Async(CancellationToken.None);
            Console.WriteLine($"✓ Server state: {serverState.ProgramState}");
            Console.WriteLine($"  - Active task: {serverState.ActiveTask?.Item2 ?? "None"}");
            Console.WriteLine($"  - Scheduled tasks: {serverState.SchedulerQueueIds?.Count ?? 0}");

            // Server control operations are commented out to avoid disrupting the server
            /*
            // Pause server
            await client.PauseServerV1Async(CancellationToken.None);
            Console.WriteLine("✓ Server paused");

            // Resume server
            await client.ResumeServerV1Async(CancellationToken.None);
            Console.WriteLine("✓ Server resumed");
            */

            Console.WriteLine("ℹ️ Server control operations are commented out to avoid disruption");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Server management demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates task management methods
    /// </summary>
    static async Task DemonstrateTaskManagement(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Task Management ===");

        try
        {
            // List active tasks
            var tasks = await client.ListTasksV1Async(CancellationToken.None);
            Console.WriteLine($"✓ Found {tasks.Length} active tasks");

            // If there are tasks, get details of the first one
            if (tasks.Length > 0 && tasks[0] is System.Text.Json.JsonElement taskElement)
            {
                if (taskElement.TryGetProperty("TaskID", out var taskIdProperty))
                {
                    var taskId = taskIdProperty.GetString();
                    if (!string.IsNullOrEmpty(taskId))
                    {
                        var taskDetails = await client.GetTaskV1Async(taskId, CancellationToken.None);
                        Console.WriteLine($"✓ Task details: {taskDetails.Status}");

                        // Task control operations are commented out to avoid disrupting running tasks
                        /*
                        // Stop task
                        await client.StopTaskV1Async(taskId, CancellationToken.None);
                        Console.WriteLine($"✓ Task {taskId} stopped");

                        // Abort task
                        await client.AbortTaskV1Async(taskId, CancellationToken.None);
                        Console.WriteLine($"✓ Task {taskId} aborted");
                        */
                    }
                }
            }

            Console.WriteLine("ℹ️ Task control operations are commented out to avoid disruption");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Task management demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates system information methods
    /// </summary>
    static async Task DemonstrateSystemInformation(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== System Information ===");

        try
        {
            // Get system information
            var systemInfo = await client.GetSystemInfoV1Async(CancellationToken.None);
            Console.WriteLine($"✓ System Info:");
            Console.WriteLine($"  - Version: {systemInfo.ServerVersionName}");
            Console.WriteLine($"  - Server Version: {systemInfo.ServerVersion}");
            Console.WriteLine($"  - Machine Name: {systemInfo.MachineName}");
            Console.WriteLine($"  - User Name: {systemInfo.UserName}");
            Console.WriteLine($"  - OS Name: {systemInfo.OSType}");
            Console.WriteLine($"  - .NET Version: {systemInfo.CLRVersion}");

            // Get changelog
            var changelog = await client.GetChangelogV1Async(CancellationToken.None);
            Console.WriteLine($"✓ Changelog: {changelog.Length} entries");

            // Get licenses
            var licenses = await client.GetLicensesV1Async(CancellationToken.None);
            Console.WriteLine($"✓ Licenses: {licenses.Length} licenses");

            // Get acknowledgements
            var acknowledgements = await client.GetAcknowledgementsV1Async(CancellationToken.None);
            Console.WriteLine($"✓ Acknowledgements: {acknowledgements.Length} acknowledgements");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ System information demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates settings management methods
    /// </summary>
    static async Task DemonstrateSettingsManagement(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Settings Management ===");

        try
        {
            // Get server settings
            var settings = await client.GetServerSettingsV1Async(CancellationToken.None);
            Console.WriteLine($"✓ Retrieved {settings.Length} server settings");

            foreach (var setting in settings.Take(5)) // Show first 5 settings
            {
                Console.WriteLine($"  - {setting.Name}: {setting.Value}");
            }

            // Settings update is commented out to avoid changing server configuration
            /*
            // Update server settings
            var settingsToUpdate = new[]
            {
                new SettingDto { Name = "example-setting", Value = "example-value" }
            };
            var updatedSettings = await client.UpdateServerSettingsV1Async(settingsToUpdate, CancellationToken.None);
            Console.WriteLine($"✓ Updated {updatedSettings.Length} settings");
            */

            Console.WriteLine("ℹ️ Settings updates are commented out to avoid changing configuration");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Settings management demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates filesystem operations
    /// </summary>
    static async Task DemonstrateFilesystemOperations(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== Filesystem Operations ===");

        try
        {
            // Browse filesystem
            var fsEntries = await client.BrowseFilesystemV1Async(CancellationToken.None);
            Console.WriteLine($"✓ Found {fsEntries.Length} filesystem entries");

            foreach (var entry in fsEntries.Take(5)) // Show first 5 entries
            {
                Console.WriteLine($"  - {entry.text} Size: ({entry.fileSize})");
            }

            // Filesystem operations are commented out to avoid file system changes
            /*
            // Perform filesystem operation
            var fsOperation = new { operation = "list", path = "/" };
            var fsResult = await client.PerformFilesystemOperationV1Async(fsOperation, CancellationToken.None);
            Console.WriteLine($"✓ Filesystem operation completed");
            */

            Console.WriteLine("ℹ️ Filesystem operations are commented out to avoid file system changes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Filesystem operations demo error: {ex.Message}");
        }
    }

    /// <summary>
    /// Demonstrates V2 API methods
    /// </summary>
    static async Task DemonstrateV2ApiMethods(DuplicatiServerClient client)
    {
        Console.WriteLine("\n=== V2 API Methods ===");

        try
        {
            var backups = await client.ListBackupsV1Async(CancellationToken.None);
            if (backups.Length == 0)
            {
                Console.WriteLine("ℹ️ No backups found to demonstrate V2 API methods");
                return;
            }

            var firstBackup = backups[0];
            Console.WriteLine($"V2 API demonstrations for backup: {firstBackup.Backup.Name}");

            // List filesets with pagination
            var filesetsRequest = new ListFilesetsRequestDto
            {
                BackupId = firstBackup.Backup.ID
            };
            var filesetsResponse = await client.ListFilesetsV2Async(filesetsRequest, CancellationToken.None);
            Console.WriteLine($"✓ V2 Filesets: {filesetsResponse!.Data!.ToArray().Length} items");

            // List folder content with pagination
            var folderRequest = new ListFolderContentRequestDto
            {
                BackupId = firstBackup.Backup.ID,
                PageSize = 10,
                Paths = null,
                Time = null,
                Page = null
            };
            var folderResponse = await client.ListFolderContentV2Async(folderRequest, CancellationToken.None);
            Console.WriteLine($"✓ V2 Folder Content: {folderResponse!.Data!.ToArray().Length} items,");

            // Search entries
            var searchRequest = new SearchEntriesRequestDto
            {
                BackupId = firstBackup.Backup.ID,
                Filters = ["*"],
                PageSize = 10,
                Paths = null,
                Time = null,
                Page = 0
            };
            var searchResponse = await client.SearchEntriesV2Async(searchRequest, CancellationToken.None);
            Console.WriteLine($"✓ V2 Search Results: {searchResponse!.Data!.ToArray().Length} items");

            // Test destination (commented out as it requires valid destination configuration)
            /*
            var destTestRequest = new DestinationTestRequestDto
            {
                BackupId = firstBackup.Backup.ID
            };
            var destTestResponse = await client.TestDestinationV2Async(destTestRequest, CancellationToken.None);
            Console.WriteLine($"✓ V2 Destination Test: {destTestResponse.Data.Success}");
            */

            Console.WriteLine("ℹ️ Destination test is commented out as it requires valid configuration");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ V2 API methods demo error: {ex.Message}");
        }
    }
}
