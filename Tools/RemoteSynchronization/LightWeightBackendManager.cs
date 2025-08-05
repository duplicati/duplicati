using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace RemoteSynchronization
{
    public class LightWeightBackendManager(string backendUrl, Dictionary<string, string> options, int maxRetries = 3, int retryDelay = 1000) : IDisposable
    {
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Program>();

        public IBackend? _backend = null;
        private readonly string _backendUrl = backendUrl;
        private int _instantiations = 0;
        private readonly int _maxRetries = maxRetries;
        private Dictionary<string, string> _options = options;
        private readonly int _retryDelay = retryDelay;
        private IStreamingBackend? _streamingBackend = null;

        public Task DeleteAsync(string remotename, CancellationToken token)
        {
            return RetryWithDelay(
                $"Delete {remotename}",
                () => _streamingBackend.DeleteAsync(remotename, token)
            );
        }

        public string DisplayName
        {
            get
            {
                if (_streamingBackend == null)
                {
                    Instantiate();
                }

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
                () => _streamingBackend.GetAsync(remotename, stream, token)
            );
        }

        private void Instantiate()
        {
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
                if (_streamingBackend == null)
                {
                    Instantiate();
                }

                try
                {
                    return _streamingBackend.ListAsync(token);
                }
                catch (Exception ex)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", ex, "Error during operation: List", null);
                    Dispose(); // Dispose current backend and streaming backend
                    Task.Delay(_retryDelay, token).Await();
                }
            }

        }

        public Task PutAsync(string remotename, Stream stream, CancellationToken token)
        {
            return RetryWithDelay(
                $"Put {remotename}",
                () => _streamingBackend.PutAsync(remotename, stream, token)
            );
        }

        public Task RenameAsync(string oldname, string newname, CancellationToken token)
        {
            if (_streamingBackend == null)
            {
                Instantiate();
            }

            return _backend switch
            {
                IStreamingBackend sb =>
                    RetryWithDelay(
                        $"Rename {oldname} to {newname}",
                        async () =>
                        {
                            // Download the file, rename it, and delete the old one
                            using var downloaded = new MemoryStream();
                            await sb.GetAsync(oldname, downloaded, token);
                            await sb.PutAsync(newname, downloaded, token);
                            await sb.DeleteAsync(oldname, token);
                        }
                    ),
                IRenameEnabledBackend ireb =>
                    RetryWithDelay(
                        $"Rename {oldname} to {newname}",
                        () => ireb.RenameAsync(oldname, newname, token)
                    ),
                _ => throw new InvalidOperationException("Backend does not support renaming."),
            };
        }

        private async Task RetryWithDelay(string operationName, Func<Task> action)
        {
            while (true)
            {
                if (_streamingBackend == null)
                {
                    Instantiate();
                }

                try
                {
                    await action();
                    return;
                }
                catch (Exception ex)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "rsync", ex, "Error during operation: {0}", operationName);
                    Dispose(); // Dispose current backend and streaming backend
                    await Task.Delay(_retryDelay);
                }
            }
        }

    }
}
