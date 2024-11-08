using System;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{
    // TODO Properly check logging
    // TODO Properly check dryrun

    internal static class FileLister
    {
        public static Task Run(string backendurl, string[] paths, IFilter filter, Options options, RestoreResults result)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Output = Channels.filesToRestore.ForWrite
            },
            async self =>
            {
                LocalRestoreDatabase db = null;
                TempFile tmpdb = null;

                try
                {
                    // If we have both target paths and a filter, combine them into a single filter
                    filter = JoinedFilterExpression.Join(new FilterExpression(paths), filter);

                    // TODO internal timers for later profiling
                    if (!options.NoLocalDb && SystemIO.IO_OS.FileExists(options.Dbpath))
                    {
                        db = new LocalRestoreDatabase(options.Dbpath);
                    }
                    else
                    {
                        tmpdb = new TempFile();
                        RecreateDatabaseHandler.NumberedFilterFilelistDelegate filelistfilter = RestoreHandler.FilterNumberedFilelist(options.Time, options.Version);
                        db = new LocalRestoreDatabase(tmpdb);

                        using (var metadatastorage = new RestoreHandlerMetadataStorage())
                        {
                            result.RecreateDatabaseResults = new RecreateDatabaseResults(result);
                            new RecreateDatabaseHandler(backendurl, options, (RecreateDatabaseResults) result.RecreateDatabaseResults)
                                .DoRun(db, false, filter, filelistfilter, null);
                        }
                    }

                    using (var backend = new BackendManager(backendurl, options, result.BackendWriter, db))
                    using (var metadatastorage = new RestoreHandlerMetadataStorage())
                    {
                        Utility.UpdateOptionsFromDb(db, options);
                        Utility.VerifyOptionsAndUpdateDatabase(db, options);

                        if (!options.NoBackendverification)
                        {
                            FilelistProcessor.VerifyRemoteList(backend, options, db, result.BackendWriter, false, null);
                        }

                        PrepareBlockAndFileList(db, options, filter);
                        CreateDirectoryStructure(db, options);
                        var files = db.GetFilesToRestore(false);

                        foreach (var file in files)
                        {
                            await self.Output.WriteAsync(file);
                        }
                    }

                    // TODO Maybe use a heap to manage the priority queue?
                }
                catch (Exception ex)
                {
                    // Check the type of exception and handle it accordingly?
                }

                // Dispose the database if it was created
                db?.Dispose();
                tmpdb?.Dispose();
            });
        }

        private static void CreateDirectoryStructure(LocalRestoreDatabase db, Options options)
        {
            if (!string.IsNullOrEmpty(options.Restorepath))
            {
                if (!SystemIO.IO_OS.DirectoryExists(options.Restorepath))
                {
                    if (!options.Dryrun)
                    {
                        SystemIO.IO_OS.DirectoryCreate(options.Restorepath);
                    }
                }
            }

            foreach (var folder in db.GetTargetFolders())
            {
                try
                {
                    if (!SystemIO.IO_OS.DirectoryExists(folder))
                    {
                        if (!options.Dryrun)
                        {
                            SystemIO.IO_OS.DirectoryCreate(folder);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create the directory {folder}: {ex.Message}");
                    throw;
                }
            }
        }

        private static void PrepareBlockAndFileList(LocalRestoreDatabase db, Options options, IFilter filter)
        {
            var c = db.PrepareRestoreFilelist(options.Time, options.Version, filter);
            if (!string.IsNullOrEmpty(options.Restorepath))
            {
                // Find the largest common prefix
                var largest_prefix = options.DontCompressRestorePaths ? "" : db.GetLargestPrefix();

                db.SetTargetPaths(largest_prefix, Util.AppendDirSeparator(options.Restorepath));
            }
            else
            {
                db.SetTargetPaths("", "");
            }

            db.FindMissingBlocks(options.SkipMetadata);
            db.CreateProgressTracker(false);
        }
    }
}