
// Copyright (C) 2026, The Duplicati Team
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
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.DatabaseTool.Commands;

/// <summary>
/// The cleanup command - removes orphaned database files
/// </summary>
public static class Cleanup
{
    /// <summary>
    /// Creates the cleanup command
    /// </summary>
    /// <returns>The cleanup command</returns>
    public static Command Create() =>
        new Command("cleanup", "Removes orphaned database files that are not referenced in dbconfig.json or the server database")
        {
            new Option<DirectoryInfo>("--datafolder", description: "The folder with databases", getDefaultValue: () => new DirectoryInfo(DataFolderLocator.GetDefaultStorageFolder(DataFolderManager.SERVER_DATABASE_FILENAME, false, true))),
            new Option<bool>("--dry-run", description: "Show what would be deleted without actually deleting", getDefaultValue: () => false),
            new Option<bool>("--force", description: "Delete without prompting for confirmation", getDefaultValue: () => false),
        }
        .WithHandler(CommandHandler.Create<DirectoryInfo, bool, bool>(async (datafolder, dryrun, force) =>
        {
            var datafolderPath = datafolder.FullName;

            // Get orphaned databases using the same logic as verify
            var orphanedDbs = await Verify.GetOrphanedDatabasesAsync(datafolderPath);

            if (orphanedDbs.Count == 0)
            {
                Console.WriteLine("No orphaned databases found.");
                return;
            }

            Console.WriteLine($"Found {orphanedDbs.Count} orphaned database(s):");
            Console.WriteLine();

            long totalSize = 0;
            foreach (var db in orphanedDbs)
            {
                var fileInfo = new FileInfo(db.Path);
                if (fileInfo.Exists)
                {
                    totalSize += fileInfo.Length;
                    Console.WriteLine($"  {db.Path}");
                    Console.WriteLine($"    Size: {Utility.FormatSizeString(fileInfo.Length)}");
                    Console.WriteLine($"    Last modified: {fileInfo.LastWriteTime}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Total size: {Utility.FormatSizeString(totalSize)}");
            Console.WriteLine();

            if (dryrun)
            {
                Console.WriteLine("Dry run mode - no files were deleted.");
                return;
            }

            // Confirm deletion unless --force is used
            if (!force)
            {
                Console.Write("Do you want to delete these orphaned databases? [y/N]: ");
                var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (response != "y" && response != "yes")
                {
                    Console.WriteLine("Operation cancelled.");
                    return;
                }
                Console.WriteLine();
            }

            // Delete orphaned databases
            int deleted = 0;
            int failed = 0;
            long freedSpace = 0;

            foreach (var db in orphanedDbs)
            {
                try
                {
                    if (File.Exists(db.Path))
                    {
                        var fileInfo = new FileInfo(db.Path);
                        var size = fileInfo.Length;

                        File.Delete(db.Path);

                        freedSpace += size;
                        deleted++;
                        Console.WriteLine($"Deleted: {db.Path}");
                    }
                    else
                    {
                        Console.WriteLine($"Already deleted: {db.Path}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"Failed to delete {db.Path}: {ex.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Cleanup complete:");
            Console.WriteLine($"  Deleted: {deleted} file(s)");
            Console.WriteLine($"  Failed: {failed} file(s)");
            Console.WriteLine($"  Space freed: {Utility.FormatSizeString(freedSpace)}");
        }));
}
