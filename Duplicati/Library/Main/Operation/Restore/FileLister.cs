using System;
using System.Linq;
using System.Threading.Tasks;
using CoCoL;
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
                    var files = db.GetFilesToRestore(true).OrderByDescending(x => x.Length).ToArray();

                    result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_DownloadingRemoteFiles);
                    // No more touching result - now only the FileProcessor updates, which locks.

                    // TODO Prioritize the files to restore.
                    foreach (var file in files)
                    {
                        await self.Output.WriteAsync(file);
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
    }
}