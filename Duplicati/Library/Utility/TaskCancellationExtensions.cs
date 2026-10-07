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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility;

/// <summary>
/// Contains extension methods for abandoning tasks that do not observe a cancellation token.
/// </summary>
public static class TaskCancellationExtensions
{
    /// <summary>
    /// Awaits a call, but stops waiting when the token is cancelled. A call that observes the
    /// token ends by itself; one that does not - a stuck file open or read, a transfer stuck in
    /// a socket write - would otherwise hold up the caller until the call gives up. The call is
    /// left to end on its own, and whatever it ends with is observed so it does not surface as
    /// an unobserved exception.
    /// </summary>
    /// <param name="task">The call to wait for</param>
    /// <param name="token">The token that stops the wait</param>
    /// <typeparam name="TResult">The result type of the call</typeparam>
    /// <returns>The result of the call</returns>
    public static Task<TResult> UntilCancelledAsync<TResult>(this Task<TResult> task, CancellationToken token)
        => UntilCancelledAsync(task, token, static _ => { });

    /// <summary>
    /// Awaits a call, but stops waiting when the token is cancelled. A call that observes the
    /// token ends by itself; one that does not - a stuck file open or read, a transfer stuck in
    /// a socket write - would otherwise hold up the caller until the call gives up. The call is
    /// left to end on its own, and whatever it ends with is observed so it does not surface as
    /// an unobserved exception.
    /// </summary>
    /// <param name="task">The call to wait for</param>
    /// <param name="token">The token that stops the wait</param>
    /// <param name="onAbandonedResult">Invoked with the result if the call completes after it was abandoned, to release what it returned</param>
    /// <typeparam name="TResult">The result type of the call</typeparam>
    /// <returns>The result of the call</returns>
    public static async Task<TResult> UntilCancelledAsync<TResult>(this Task<TResult> task, CancellationToken token, Action<TResult> onAbandonedResult)
    {
        try
        {
            return await task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The continuation also runs if the call has completed in the meantime,
            // so a result that arrives just as the wait is cancelled is released as well
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _ = t.Exception;
                else if (t.IsCompletedSuccessfully)
                    try { onAbandonedResult(t.Result); }
                    catch { }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
}
