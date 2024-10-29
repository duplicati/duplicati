namespace Duplicati.Library.RemoteControl;

/// <summary>
/// Helper class to support period refreshes, with signal support
/// </summary>
public class PeriodicRefresher : IDisposable
{
    /// <summary>
    /// The last time the refresh was done
    /// </summary>
    private DateTime _lastRefresh = DateTime.MinValue;
    /// <summary>
    /// The interval between refreshes
    /// </summary>
    private readonly TimeSpan _refreshInterval;
    /// <summary>
    /// The minimum interval between refreshes (debounce)
    /// </summary>
    private readonly TimeSpan _minimumRefreshInterval;
    /// <summary>
    /// The action to run on refresh
    /// </summary>
    private readonly Func<CancellationToken, Task> _refreshAction;
    /// <summary>
    /// The cancellation token source
    /// </summary>
    private readonly CancellationTokenSource _cts;
    /// <summary>
    /// The task completion source used to signal a refresh
    /// </summary>
    private TaskCompletionSource<bool> _tcs;

    /// <summary>
    /// Creates a new instance of the class
    /// </summary>
    /// <param name="refreshInterval">The interval between refreshes</param>
    /// <param name="minimumRefresh">The minimum interval between refreshes (debounce)</param>
    /// <param name="refreshAction">The action to run on refresh</param>
    /// <param name="cancellationToken">The cancellation token to use</param>
    public PeriodicRefresher(TimeSpan refreshInterval, TimeSpan minimumRefresh, Func<CancellationToken, Task> refreshAction, CancellationToken cancellationToken)
    {
        _refreshInterval = refreshInterval;
        _minimumRefreshInterval = minimumRefresh;
        _refreshAction = refreshAction;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _tcs = new TaskCompletionSource<bool>();
    }

    /// <summary>
    /// Signals a refresh
    /// </summary>
    public void Signal()
    {
        _tcs.TrySetResult(true);
    }

    /// <summary>
    /// Runs the refresh loop
    /// </summary>
    /// <returns>The task to await</returns>
    public async Task RunLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var t = await Task.WhenAny(_tcs.Task, Task.Delay(_refreshInterval, _cts.Token));
            if (_cts.Token.IsCancellationRequested)
                return;

            if (t == _tcs.Task)
                Interlocked.Exchange(ref _tcs, new TaskCompletionSource<bool>());

            if ((_lastRefresh + _minimumRefreshInterval) < DateTime.Now)
            {
                _lastRefresh = DateTime.Now;
                await _refreshAction(_cts.Token);
            }
        }
    }

    /// </inheritdoc>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
