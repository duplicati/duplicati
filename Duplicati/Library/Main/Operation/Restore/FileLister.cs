using System;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class FileLister
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<FileLister>();

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
                catch (RetiredException)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RetiredProcess", null, "File lister retired");
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "FileListerError", ex, "Error during file listing");
                    throw;
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
                        if (options.Dryrun)
                        {
                            Logging.Log.WriteDryrunMessage(LOGTAG, "DryrunCreateDirectory", $"Would have created the directory {folder}");
                        }
                        else
                        {
                            SystemIO.IO_OS.DirectoryCreate(folder);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "CreateDirectoryError", ex, $"Failed to create the directory {folder}");
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