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


using System.Runtime.CompilerServices;

namespace Duplicati.Library.SourceProvider.ChangeTracking;

/// <summary>
/// Routes the failures of listings and downloads to the change tracking key that owns them,
/// so the key is not advanced and the items are reported again by the next backup
/// </summary>
public static class ChangeTrackerExtensions
{
    /// <summary>
    /// Opens a content stream and reports a failure to open or read it to the change tracker,
    /// so the token of the folder or drive the content belongs to is not advanced and the item
    /// is reported again next time. This includes content that answers not found: an item the
    /// listing just reported is more likely to be missing transiently than to be gone, and if it
    /// is gone the walk from the held-back token reports it as removed next time, at the cost of
    /// repeating one incremental walk. The failure is recorded against the item, so an item
    /// that keeps failing is eventually given up on instead of pinning its whole tree.
    /// </summary>
    /// <param name="tracker">The change tracker</param>
    /// <param name="stateKey">The change tracking key of the folder or drive, or <c>null</c> if changes are not tracked</param>
    /// <param name="item">What identifies the item below the key, such as the entry path or the item id; the same item must be identified the same way wherever it can fail</param>
    /// <param name="factory">Opens the stream</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The stream</returns>
    public static async Task<Stream> OpenWithFailureTrackingAsync(this ChangeTracker tracker, string? stateKey, string item, Func<CancellationToken, Task<Stream>> factory, CancellationToken cancellationToken)
    {
        if (stateKey == null || !tracker.IsActive)
            return await factory(cancellationToken).ConfigureAwait(false);

        Stream stream;
        try
        {
            stream = await factory(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            tracker.MarkItemFailed(stateKey, item);
            throw;
        }

        return new FailureReportingStream(stream, () => tracker.MarkItemFailed(stateKey, item));
    }

    /// <summary>
    /// Lists the children of a folder and reports a failure to list them to the change
    /// tracker, so the token of the drive the folder belongs to is not advanced and the
    /// folder is listed again next time. The backup skips a folder it cannot list and
    /// continues with the rest, so without this the drive would store a token claiming the
    /// folder was walked. The failure counts against the whole key, never as an item failure
    /// that is eventually given up on: nothing below the folder was seen, so advancing the
    /// token past it would leave the folder's contents out of the backup until each of them
    /// changes.
    /// </summary>
    /// <typeparam name="T">The type of the items</typeparam>
    /// <param name="tracker">The change tracker</param>
    /// <param name="stateKey">The change tracking key of the drive, or <c>null</c> if changes are not tracked</param>
    /// <param name="source">The listing</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The items</returns>
    public static IAsyncEnumerable<T> ListWithFailureTrackingAsync<T>(this ChangeTracker tracker, string? stateKey, IAsyncEnumerable<T> source, CancellationToken cancellationToken)
        => stateKey == null || !tracker.IsActive
            ? source
            : ListReportingFailuresAsync(source, () => tracker.MarkStateFailed(stateKey), cancellationToken);

    /// <summary>
    /// Lists the entries derived from one item, such as the revisions of a file, and reports
    /// a failure to list them against that item, so the token is not advanced and the item is
    /// reported again next time, while an item whose derived entries can never be listed is
    /// eventually given up on like an item whose content cannot be read. The item itself was
    /// listed, so only what is derived from it is missing while the failure lasts.
    /// </summary>
    /// <typeparam name="T">The type of the entries</typeparam>
    /// <param name="tracker">The change tracker</param>
    /// <param name="stateKey">The change tracking key of the drive, or <c>null</c> if changes are not tracked</param>
    /// <param name="item">What identifies the item below the key, such as its path or id</param>
    /// <param name="source">The listing</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    public static IAsyncEnumerable<T> ListItemWithFailureTrackingAsync<T>(this ChangeTracker tracker, string? stateKey, string item, IAsyncEnumerable<T> source, CancellationToken cancellationToken)
        => stateKey == null || !tracker.IsActive
            ? source
            : ListReportingFailuresAsync(source, () => tracker.MarkItemFailed(stateKey, item), cancellationToken);

    /// <summary>
    /// Enumerates a source and calls back once on the first exception the enumeration throws,
    /// before rethrowing it
    /// </summary>
    /// <typeparam name="T">The type of the items</typeparam>
    /// <param name="source">The listing</param>
    /// <param name="onFailure">Called when the enumeration fails</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The items</returns>
    private static async IAsyncEnumerable<T> ListReportingFailuresAsync<T>(IAsyncEnumerable<T> source, Action onFailure, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                onFailure();
                throw;
            }

            if (!hasNext)
                yield break;

            yield return enumerator.Current;
        }
    }
}
