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
    public class LightWeightBackendManager(string backendUrl, Dictionary<string, string> options, int maxRetries = 3, int retryDelay = 1000, bool autoCreateFolders = false, bool retryWithExponentialBackoff = false) : IDisposable
    {
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Program>();

        public IBackend? _backend = null;

        private bool _anyDownloaded = false;
        private bool _anyUploaded = false;
        private readonly string _backendUrl = backendUrl;
        private int _instantiations = 0;
        private readonly int _maxRetries = maxRetries;
        private Dictionary<string, string> _options = options;
        private int _retryDelay = retryDelay;
        private IStreamingBackend? _streamingBackend = null;

        public Task DeleteAsync(string remotename, CancellationToken token)
        {
            return RetryWithDelay(
                $"Delete {remotename}",
                () => _streamingBackend.DeleteAsync(remotename, token),
                token
            );
        }

        public string DisplayName
        {
            get
            {
                Instantiate();

                return _streamingBackend.DisplayName;
            }
        }

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

        public Task GetAsync(string remotename, Stream stream, CancellationToken token)
        {
            return RetryWithDelay(
                $"Get {remotename}",
                () => _streamingBackend
                    .GetAsync(remotename, stream, token)
                    .ContinueWith(_ => _anyDownloaded = true, token),
                token
            );
        }

        private void Instantiate()
        {
            if (_backend != null)
            {
                // If we already have a backend, we can just return.
                return;
            }

            if (_instantiations < _maxRetries)
            {
                _backend = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(_backendUrl, _options);
                _streamingBackend = _backend as IStreamingBackend ?? throw new InvalidOperationException("Backend does not support streaming operations.");
                _instantiations++;
            }
            else
            {
                throw new InvalidOperationException("Maximum number of backend instantiations reached.");
            }
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken token)
        {
            while (true)
            {
                Instantiate();

                try
                {
                    return _streamingBackend.ListAsync(token);
                }
                catch (Exception ex)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", ex, "Error during operation: List", null);
                    Dispose(); // Dispose current backend and streaming backend

                    TryRecoverFromException(ex, token).Await();
                }
            }

        }

        public Task PutAsync(string remotename, Stream stream, CancellationToken token)
        {
            return RetryWithDelay(
                $"Put {remotename}",
                () => _streamingBackend
                    .PutAsync(remotename, stream, token)
                    .ContinueWith(_ => _anyUploaded = true, token),
                token
            );
        }

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
                            await sb.PutAsync(newname, downloaded, token).ConfigureAwait(false);
                            await sb.DeleteAsync(oldname, token).ConfigureAwait(false);
                            _anyUploaded = true;
                            _anyDownloaded = true;
                        },
                        token
                    ),
                IRenameEnabledBackend ireb =>
                    RetryWithDelay(
                        $"Rename {oldname} to {newname}",
                        () => ireb
                            .RenameAsync(oldname, newname, token)
                            .ContinueWith(
                                _ =>
                                {
                                    _anyUploaded = true;
                                    _anyDownloaded = true;
                                },
                                token
                            ),
                        token
                    ),
                _ => throw new InvalidOperationException("Backend does not support renaming."),
            };
        }

        private async Task RetryWithDelay(string operationName, Func<Task> action, CancellationToken token)
        {
            while (true)
            {
                // This will throw an exception if we've reached the max
                // number of retries.
                Instantiate();

                try
                {
                    await action().ConfigureAwait(false);
                    return; // Exit the loop if the action succeeds.
                }
                catch (Exception ex)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", ex, "Error during operation: {0}", operationName);
                    Dispose(); // Dispose current backend and streaming backend.

                    // Try to see if we can recover from the error.
                    await TryRecoverFromException(ex, token).ConfigureAwait(false);
                }
            }
        }

        private async Task<bool> TryCreateFolder(CancellationToken token)
        {
            try
            {
                Instantiate();

                // Attempt to create the folder
                await _backend.CreateFolderAsync(token).ConfigureAwait(false);

                return true; // Folder creation succeeded
            }
            catch (Exception ex)
            {
                Dispose();

                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", ex, "Failed to create folder", null);

                return false; // Folder creation failed
            }
        }

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

                    foreach (var name in await _backend.GetDNSNamesAsync(token).ConfigureAwait(false) ?? [])
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
                if (retryWithExponentialBackoff)
                    _retryDelay <<= 1; // Double the delay for exponential backoff

                await Task.Delay(_retryDelay, token).ConfigureAwait(false);
            }
        }

    }
}
