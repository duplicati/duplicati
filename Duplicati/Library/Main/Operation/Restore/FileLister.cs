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
        public static Task Run(LocalRestoreDatabase db, BackendManager backend, IFilter filter, Options options, RestoreResults result)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Output = Channels.filesToRestore.ForWrite
            },
            async self =>
            {
                try
                {
                    using (var metadatastorage = new RestoreHandlerMetadataStorage())
                    {
                        Utility.UpdateOptionsFromDb(db, options);
                        Utility.VerifyOptionsAndUpdateDatabase(db, options);

                        if (!options.NoBackendverification)
                        {
                            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PreRestoreVerify);
                            FilelistProcessor.VerifyRemoteList(backend, options, db, result.BackendWriter, false, null);
                        }

                        result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateFileList);
                        PrepareBlockAndFileList(db, options, filter, result);
                        result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateTargetFolders);
                        CreateDirectoryStructure(db, options);
                        var files = db.GetFilesToRestore(false);

                        result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_DownloadingRemoteFiles);
                        // No more touching result - now only the FileProcessor updates, which locks.

                        // TODO Prioritize the files to restore.
                        foreach (var file in files)
                        {
                            await self.Output.WriteAsync(file);
                        }
                    }
                    // TODO Maybe use a heap to manage the priority queue if it changes during runtime?
                }
                catch (RetiredException ex)
                {
                    // Check the type of exception and handle it accordingly?
                }
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

        private static void PrepareBlockAndFileList(LocalRestoreDatabase db, Options options, IFilter filter, RestoreResults result)
        {
            var c = db.PrepareRestoreFilelist(options.Time, options.Version, filter);
            result.OperationProgressUpdater.UpdatefileCount(c.Item1, c.Item2, true);

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