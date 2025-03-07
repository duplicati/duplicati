#nullable enable

using Duplicati.Library.Backend.Backblaze;
using Duplicati.Library.Backend.Backblaze.Model;

namespace Backblaze;

internal class B2SharedState : IDisposable
{
    public sealed record Values(
        Dictionary<string, List<FileEntity>>? FileCache,
        B2AuthHelper? AuthHelper);

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    private Values _values = new Values(null, null);

    /// <summary>
    /// Gets the file entry for the specified filename, or null if not found.
    /// </summary>
    /// <param name="filename">The filename to locate</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The file entry, or null if not found</returns>
    public async Task<FileEntity?> GetFileEntityFromCache(string filename)
    {
        FileEntity? result = null;
        await RentStateAsync(x =>
            {
                if (x.FileCache != null && x.FileCache.TryGetValue(filename, out var value))
                    result = value.OrderByDescending(x => x.UploadTimestamp).First();

                return x;

            }).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Gets the file entry for the specified filename, or null if not found.
    /// </summary>
    /// <param name="filename">The filename to locate</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The file entry, or null if not found</returns>
    public async Task<List<FileEntity>?> GetFileEntitiesFromCache(string filename)
    {
        List<FileEntity>? result = null;
        await RentStateAsync(x =>
            {
                if (x.FileCache != null && x.FileCache.TryGetValue(filename, out var value))
                    result = [.. value];

                return x;

            }).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Clears the file cache
    /// </summary>
    /// <returns>A task that completes when the cache is cleared</returns>
    public Task ClearFileCache()
        => RentStateAsync(x => x with { FileCache = null });

    /// <summary>
    /// Gets exclusive access to the shared state and performs an action
    /// </summary>
    /// <param name="action">The action to perform</param>
    /// <returns>A task that completes when the action is done</returns>
    public async Task RentStateAsync(Func<Values, Values> action)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _values = action(_values);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets exclusive access to the shared state and performs an action
    /// </summary>
    /// <param name="action">The action to perform</param>
    /// <returns>A task that completes when the action is done</returns>
    public async Task RentStateAsync(Func<Values, Task<Values>> action)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _values = await action(_values).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets exclusive access to the shared state and performs an action
    /// </summary>
    /// <param name="action">The action to perform</param>
    /// <returns>A task that completes when the action is done</returns>
    public void RentState(Func<Values, Values> action)
    {
        _semaphore.Wait();
        try
        {
            _values = action(_values);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _semaphore.Wait();
        try
        {
            _values.AuthHelper?.HttpClient.Dispose();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
