using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace RemoteSynchronization
{
    /// <summary>
    /// A lightweight backend manager that handles remote synchronization operations.
    /// This class is designed to manage backend operations such as Get, Put, Delete, Rename, and List asynchronously.
    /// It supports retrying operations with a specified delay and can automatically create folders if they do not exist.
    /// The class implements IDisposable to ensure proper resource management.
    /// It uses a streaming backend for efficient file operations and handles exceptions gracefully to allow for retries and recovery.
    /// A retry incurs a new instantiation of the backend.
    /// It is designed to mimic the behavior of Duplicati.Library.Main.Backend.BackendManager.
    /// </summary>
    /// <param name="backendUrl">The backend URL string.</param>
    /// <param name="options">A dictionary of options to pass to the backend.</param>
    /// <param name="maxRetries">The maximum number of retries for failed operations.</param>
    /// <param name="retryDelay">The delay between retries, in milliseconds.</param>
    /// <param name="autoCreateFolders">Whether to automatically create folders if they do not exist.</param>
    /// <param name="retryWithExponentialBackoff">Whether to use exponential backoff for retries.</param>
    public class LightWeightBackendManager(string backendUrl, Dictionary<string, string> options, int maxRetries = 3, int retryDelay = 1000, bool autoCreateFolders = false, bool retryWithExponentialBackoff = false) : IDisposable
    {
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Program>();

        public IBackend? _backend = null;

        private bool _anyDownloaded = false;
        private bool _anyUploaded = false;
        private readonly string _backendUrl = backendUrl;
        //private int _instantiations = 0;
        private readonly int _maxRetries = maxRetries;
        private readonly Dictionary<string, string> _options = options;
        private int _retryDelay = retryDelay;
        private int _currentRetryDelay = retryDelay;
        private IStreamingBackend? _streamingBackend = null;

        /// <summary>
        /// Deletes a file from the remote backend.
        /// </summary>
        /// <param name="remotename">The name of the remote file to delete.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous delete operation.</returns>
        public Task DeleteAsync(string remotename, CancellationToken token)
        {
            return RetryWithDelay(
                $"Delete {remotename}",
                () => _streamingBackend!.DeleteAsync(remotename, token),
                null,
                false,
                token
            );
        }

        /// <summary>
        /// Gets the display name of the backend.
        /// This property initializes the backend if it has not been instantiated yet.
        /// </summary>
        /// <returns>The display name of the backend.</returns>
        public string DisplayName
        {
            get
            {
                Instantiate();

                return _streamingBackend!.DisplayName;
            }
        }

        /// <summary>
        /// Disposes of the backend and streaming backend resources.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _streamingBackend?.Dispose();
                _streamingBackend = null;
                _backend?.Dispose();
                _backend = null;
            }
            catch (Exception ex)
            {
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", ex, "Error during Dispose", null);
            }
        }

        /// <summary>
        /// Gets a file from the remote backend and writes it to the specified stream.
        /// This method retries the operation with a delay if it fails.
        /// </summary>
        /// <param name="remotename">The name of the remote file to get.</param>
        /// <param name="stream">The stream to write the file to.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous get operation.</returns>
        public Task GetAsync(string remotename, Stream stream, CancellationToken token)
        {
            return RetryWithDelay(
                $"Get {remotename}",
                async () =>
                {
                    await _streamingBackend!.GetAsync(remotename, stream, token).ConfigureAwait(false);
                    _anyDownloaded = true;
                },
                stream,
                true,
                token
            );
        }

        /// <summary>
        /// Instantiates the backend if it has not been instantiated yet.
        /// If the backend is already instantiated, it simply returns.
        /// If the maximum number of retries has been reached, it throws an InvalidOperationException.
        /// This method is called internally to ensure that the backend is ready for operations.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the maximum number of instantiations has been reached.</exception>
        private void Instantiate()
        {
            if (_backend != null)
            {
                // If we already have a backend, we can just return.
                return;
            }

            _backend = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(_backendUrl, _options);
            _streamingBackend = _backend as IStreamingBackend;
            if (_streamingBackend == null || !_streamingBackend.SupportsStreaming)
            {
                _backend.Dispose();
                _backend = null;
                throw new InvalidOperationException("Backend does not support streaming operations.");
            }
        }

        /// <summary>
        /// Lists the files in the remote backend asynchronously.
        /// </summary>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A list of the file entries on the remote backend.</returns>
        public async Task<List<IFileEntry>> ListAsync(CancellationToken token)
        {
            // TODO It would be more graceful if this method returned an
            // IAsyncEnumerable instead, capturing failures along the way,
            // Followed by retrying / resuming the listing from where it
            // crashed. Current "workaround" is to build the entire list before
            // returning it.
            List<IFileEntry> entries = [];
            await RetryWithDelay("List", async () =>
                {
                    entries = await _streamingBackend!.ListAsync(token).ToListAsync().ConfigureAwait(false);
                },
                null,
                false,
                token)
                .ConfigureAwait(false);

            return entries;
        }

        /// <summary>
        /// Puts a file to the remote backend from the specified stream.
        /// </summary>
        /// <param name="remotename">The name of the remote file to put.</param>
        /// <param name="stream">The stream containing the file data to put.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous put operation.</returns>
        public Task PutAsync(string remotename, Stream stream, CancellationToken token)
        {
            return RetryWithDelay(
                $"Put {remotename}",
                async () =>
                {
                    await _streamingBackend!.PutAsync(remotename, stream, token).ConfigureAwait(false);
                    _anyUploaded = true;
                },
                stream,
                false,
                token
            );
        }

        /// <summary>
        /// Renames a file in the remote backend.
        /// If the backend supports renaming, it uses the RenameAsync method.
        /// If the backend does not support renaming, it downloads the file, renames it, and deletes the old one.
        /// </summary>
        /// <param name="oldname">The current name of the remote file.</param>
        /// <param name="newname">The new name for the remote file.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous rename operation.</returns>
        public Task RenameAsync(string oldname, string newname, CancellationToken token)
        {
            Instantiate();

            return _backend switch
            {
                IStreamingBackend sb =>
                    RetryWithDelay(
                        $"Rename {oldname} to {newname}",
                        async () =>
                        {
                            // Download the file, rename it, and delete the old one
                            using var downloaded = new MemoryStream();
                            await sb.GetAsync(oldname, downloaded, token).ConfigureAwait(false);
                            downloaded.Seek(0, SeekOrigin.Begin);
                            await sb.PutAsync(newname, downloaded, token).ConfigureAwait(false);
                            await sb.DeleteAsync(oldname, token).ConfigureAwait(false);
                            _anyUploaded = true;
                            _anyDownloaded = true;
                        },
                        null,
                        false,
                        token
                    ),
                IRenameEnabledBackend ireb =>
                    RetryWithDelay(
                        $"Rename {oldname} to {newname}",
                        async () =>
                        {
                            await ireb.RenameAsync(oldname, newname, token).ConfigureAwait(false);
                            _anyUploaded = true;
                            _anyDownloaded = true;
                        },
                        null,
                        false,
                        token
                    ),
                _ => throw new InvalidOperationException("Backend does not support renaming."),
            };
        }

        /// <summary>
        /// Retries an operation with a delay if it fails.
        /// This method will instantiate the backend if it has not been instantiated yet.
        /// If the operation fails, it will log the error, dispose of the current backend and streaming backend,
        /// reset the stream if specified, and attempt to recover from the exception.
        /// If recovery is not possible, it will wait for the specified retry delay before retrying the operation.
        /// The retry delay can be increased exponentially if specified.
        /// </summary>
        /// <param name="operationName">The name of the operation being retried, used for logging.</param>
        /// <param name="action">The action to perform.</param>
        /// <param name="stream">The stream to use for the operation. Used when resetting the stream during recovery.</param>
        /// <param name="resetStream">Whether to reset the stream if the operation fails.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task RetryWithDelay(string operationName, Func<Task> action, Stream? stream, bool resetStream, CancellationToken token)
        {
            int instantiations = 0;
            _currentRetryDelay = _retryDelay; // Reset the current retry delay to the initial value

            do
            {
                // This will throw an exception if we've reached the max
                // number of retries.
                Instantiate();
                instantiations++;

                try
                {
                    await action().ConfigureAwait(false);
                    return; // Exit the loop if the action succeeds.
                }
                catch (Exception ex)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", ex, "Error during operation: {0}", operationName);
                    Dispose(); // Dispose current backend and streaming backend.

                    // Reset the stream, as it's in a potentially faulty state.
                    if (stream != null && stream.CanSeek)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        if (resetStream)
                            stream.SetLength(0);
                    }

                    // Try to see if we can recover from the error.
                    await TryRecoverFromException(ex, token).ConfigureAwait(false);
                }
            } while (instantiations < _maxRetries);

            // If we reach here, it means all retries failed.
            throw new InvalidOperationException($"Operation '{operationName}' failed after {instantiations} attempts.");
        }

        /// <summary>
        /// Attempts to create a folder in the remote backend.
        /// </summary>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is true if the folder was created successfully, false otherwise.</returns>
        private async Task<bool> TryCreateFolder(CancellationToken token)
        {
            bool created = false;
            await RetryWithDelay("CreateFolder", async () =>
                {
                    try
                    {
                        await _backend!.CreateFolderAsync(token).ConfigureAwait(false);
                        created = true; // Folder creation succeeded
                    }
                    catch
                    {
                        created = false; // Folder creation failed
                    }
                },
                null, false, token);

            return created;
        }

        /// <summary>
        /// Attempts to recover from an exception that occurred during a backend operation.
        /// This method checks for specific types of exceptions, such as DNS resolution failures or folder missing exceptions.
        /// If a DNS failure is detected, it attempts to refresh the DNS name by re-instantiating the backend and resolving DNS names.
        /// If the exception is a folder missing exception and auto-creation of folders is enabled, it attempts to create the folder.
        /// If recovery is not possible, it waits for the specified retry delay.
        /// The retry delay will be doubled if exponential backoff is enabled.
        /// </summary>
        /// <param name="ex">The exception that occurred during the operation.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous recovery operation.</returns>
        private async Task TryRecoverFromException(Exception ex, CancellationToken token)
        {
            // Copied from Duplicati.Library.Main.Backend.BackendManager.Handler.

            // Refresh DNS name if we fail to connect in order to prevent issues with incorrect DNS entries.
            var dnsFailure = ExceptionExtensions.FlattenException(ex)
            .Any(x =>
                (x is System.Net.WebException wex && wex.Status == System.Net.WebExceptionStatus.NameResolutionFailure)
                ||
                (x is System.Net.Sockets.SocketException sockEx && sockEx.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound)
            );

            if (dnsFailure)
            {
                try
                {
                    Instantiate();

                    foreach (var name in await _backend!.GetDNSNamesAsync(token).ConfigureAwait(false) ?? [])
                        if (!string.IsNullOrWhiteSpace(name))
                            System.Net.Dns.GetHostEntry(name);
                }
                catch { }
            }

            var recovered = false;

            // Check if this was a folder missing exception and we are allowed to autocreate folders
            if (!(_anyDownloaded || _anyUploaded) && autoCreateFolders && ExceptionExtensions.FlattenException(ex).Any(x => x is FolderMissingException))
            {
                if (await TryCreateFolder(token).ConfigureAwait(false))
                    recovered = true;
            }

            // Finally, if we did not recover, wait the specified delay before retrying.
            if (!recovered && _retryDelay > 0)
            {
                await Task.Delay(_currentRetryDelay, token).ConfigureAwait(false);

                if (retryWithExponentialBackoff)
                    _currentRetryDelay <<= 1; // Double the delay for exponential backoff
            }
        }

    }
}
