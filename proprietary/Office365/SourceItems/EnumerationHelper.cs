// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.Office365.SourceItems;

/// <summary>
/// Helpers for enumerating Graph collections from source entries.
/// </summary>
internal static class EnumerationHelper
{
    /// <summary>
    /// Enumerates <paramref name="source"/> and ends the enumeration quietly when it
    /// throws an exception accepted by <paramref name="shouldSkip"/>. Any other exception
    /// propagates as usual. This lets a source entry treat a known condition, such as a
    /// user without a mailbox, as "nothing to enumerate" without buffering the items.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The enumerable to read from.</param>
    /// <param name="shouldSkip">Decides whether an exception ends the enumeration quietly.</param>
    /// <param name="onSkip">Invoked with the exception when the enumeration is ended quietly.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The items from <paramref name="source"/> up to the skipped failure.</returns>
    public static async IAsyncEnumerable<T> EndOnError<T>(
        IAsyncEnumerable<T> source,
        Func<Exception, bool> shouldSkip,
        Action<Exception> onSkip,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && shouldSkip(ex))
            {
                onSkip(ex);
                yield break;
            }

            if (!hasNext)
                yield break;

            yield return enumerator.Current;
        }
    }
}
