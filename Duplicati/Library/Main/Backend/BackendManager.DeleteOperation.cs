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

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending DELETE operation
    /// </summary>
    private class DeleteOperation : PendingOperation<bool>
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<DeleteOperation>();

        /// <summary>
        /// The remote filename that is to be deleted
        /// </summary>
        public override string RemoteFilename { get; }
        /// <summary>
        /// The size of the remote file, or -1 if unknown
        /// </summary>
        public override long Size { get; }

        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.Delete;

        /// <summary>
        /// Creates a new DeleteOperation
        /// </summary>
        /// <param name="remoteName">The remote filename to delete</param>
        /// <param name="size">The size of the remote file, or -1 if unknown</param>
        /// <param name="context">The execution context</param>
        /// <param name="waitForComplete">Whether to wait for the operation to complete</param>
        public DeleteOperation(string remoteName, long size, ExecuteContext context, bool waitForComplete, CancellationToken cancelToken)
            : base(context, waitForComplete, cancelToken)
        {
            RemoteFilename = remoteName;
            Size = size;
        }

        /// <summary>
        /// Generates a unique filename by adding a random suffix if the target already exists.
        /// </summary>
        /// <param name="baseName">The base filename</param>
        /// <returns>A unique filename</returns>
        private static string GenerateUniqueFilename(string baseName)
        {
            return baseName + $".{Guid.NewGuid().ToString("N")[..6]}";
        }

        /// <summary>
        /// Performs the soft-delete operation by renaming the file with the soft-delete prefix.
        /// Handles creating target directories and resolving filename conflicts.
        /// </summary>
        /// <param name="backend">The backend to use</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>True if the operation succeeded</returns>
        private async Task<bool> PerformSoftDeleteAsync(IBackend backend, CancellationToken cancelToken)
        {
            // The effective name is the name the backend understands (the filename part
            // for non-folder backends, the full relative path for folder-enabled ones).
            // The soft-delete prefix is applied to this effective name, and the rename/
            // copy happens within the backend URL the operation is bound to.
            var effectiveName = GetEffectiveRemoteName();
            var softDeletePrefix = Context.Options.SoftDeletePrefix!;
            var newName = softDeletePrefix + effectiveName;
            var newNameWithSuffix = newName;
            const int maxAttempts = 3;

            // Try to rename the file
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (backend is IRenameEnabledBackend renameBackend && !Context.Options.PreventBackendRename)
                    {
                        await renameBackend.RenameAsync(effectiveName, newNameWithSuffix, cancelToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // Soft-delete fallback: download, upload with new name, and delete old file
                        using (var tempFile = new Library.Utility.TempFile())
                        {
                            await backend.GetAsync(effectiveName, tempFile, cancelToken).ConfigureAwait(false);
                            await backend.PutAsync(newName, tempFile, cancelToken).ConfigureAwait(false);
                        }

                        await backend.DeleteAsync(effectiveName, cancelToken).ConfigureAwait(false);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "RemoteFileRenameFailed", ex, $"Failed to rename file {RemoteFilename} to {newName}, attempt {attempt + 1}/{maxAttempts}");

                    if (attempt == maxAttempts - 1)
                        throw;

                    // Try with a unique suffix in case the target file already exists
                    newNameWithSuffix = GenerateUniqueFilename(newName);
                }
            }

            throw new Exception("Failed to perform soft-delete on file");
        }

        /// <inheritdoc/>
        public override async Task<bool> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            // The effective name is the name the backend understands (the filename part
            // for non-folder backends, the full relative path for folder-enabled ones).
            var effectiveName = GetEffectiveRemoteName();

            Context.Statwriter.SendEvent(BackendActionType.Delete, BackendEventType.Started, RemoteFilename, Size);

            string? result = null;
            bool deleteSucceeded = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(Context.Options.SoftDeletePrefix))
                {
                    await PerformSoftDeleteAsync(backend, cancelToken).ConfigureAwait(false);
                }
                else
                {
                    await backend.DeleteAsync(effectiveName, cancelToken).ConfigureAwait(false);
                }
                deleteSucceeded = true;
            }
            catch (Exception ex)
            {
                // Check if the file was not found, and if so, check if it was actually deleted
                var isFileMissingException = ex is Library.Interface.FileMissingException || ex is System.IO.FileNotFoundException;
                var wr = (ex as System.Net.WebException)?.Response as System.Net.HttpWebResponse;
                var isWrFileMissingException = wr != null && (wr.StatusCode == System.Net.HttpStatusCode.NotFound || wr.StatusCode == System.Net.HttpStatusCode.Gone);
                var isHttpFileMissingException = ex is HttpRequestException && (ex as HttpRequestException)?.StatusCode == System.Net.HttpStatusCode.NotFound;
                bool recovered = false;

                if (isFileMissingException || isWrFileMissingException || isHttpFileMissingException)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileFailed", LC.L($"Delete operation failed for {RemoteFilename} with FileNotFound, listing contents"));

                    try
                    {
                        // The backend is bound to the file's folder (for non-folder backends)
                        // or the root (for folder-enabled backends), so the listing returns
                        // names comparable to the effective remote name.
                        recovered = !await backend.ListAsync(cancelToken).Select(x => x.Name).ContainsAsync(effectiveName).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    if (recovered)
                    {
                        Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileSuccess", LC.L($"Listing indicates file {RemoteFilename} was deleted correctly"));
                        deleteSucceeded = true;
                    }
                    else
                        Logging.Log.WriteWarningMessage(LOGTAG, "DeleteRemoteFileFailed", ex, LC.L($"Listing confirms file {RemoteFilename} was not deleted"));
                }

                if (!recovered)
                {
                    result = ex.ToString();
                    throw;
                }
            }
            finally
            {
                // Here, we do not know if the file was actually deleted or not
                // We log that the operation was performed, and the result
                Context.Database.LogRemoteOperation("delete", RemoteFilename, result);

                if (deleteSucceeded)
                {
                    // We also log the new state of the file, so it will be attempted to be re-deleted on later listings
                    Context.Database.LogRemoteVolumeUpdated(RemoteFilename, RemoteVolumeState.Deleted, -1, null);
                }
            }

            // Best-effort: also delete the parity companion of a data volume, if parity
            // is enabled. Parity files are not tracked in the database, so we delete them
            // directly by their derived name. A missing parity file is harmless.
            if (deleteSucceeded)
                await MaybeDeleteParityAsync(backend, cancelToken).ConfigureAwait(false);

            Context.Statwriter.SendEvent(BackendActionType.Delete, BackendEventType.Completed, RemoteFilename, Size);
            return true;
        }

        /// <summary>
        /// Deletes the parity companion of the just-deleted data volume, if parity is enabled.
        /// </summary>
        /// <param name="backend">The backend to delete from</param>
        /// <param name="cancelToken">The cancellation token</param>
        private async Task MaybeDeleteParityAsync(IBackend backend, CancellationToken cancelToken)
        {
            var parityModule = Context.Options.ParityModule;
            if (string.IsNullOrEmpty(parityModule))
                return;

            // Only data volumes (dblock/dlist) have parity companions; skip index files,
            // parity files themselves, and anything that does not parse as a volume.
            var parsed = Volumes.VolumeBase.ParseFilename(RemoteFilename);
            if (parsed == null || parsed.IsParity)
                return;
            if (parsed.FileType != RemoteVolumeType.Blocks && parsed.FileType != RemoteVolumeType.Files)
                return;

            // The parity module key is also its filename extension.
            var parityEffectiveName = GetEffectiveRemoteName() + "." + parityModule;
            try
            {
                await backend.DeleteAsync(parityEffectiveName, cancelToken).ConfigureAwait(false);
                Logging.Log.WriteVerboseMessage(LOGTAG, "ParityDeleted", "Deleted parity companion for {0}", RemoteFilename);
            }
            catch (Exception ex)
            {
                // A missing parity file (or any delete failure) must not fail the operation
                Logging.Log.WriteVerboseMessage(LOGTAG, "ParityDeleteFailed", ex, "Could not delete parity companion for {0}: {1}", RemoteFilename, ex.Message);
            }
        }
    }
}
