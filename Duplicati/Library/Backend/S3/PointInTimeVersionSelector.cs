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

[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// One version of an object, as reported by a version listing
    /// </summary>
    /// <param name="Key">The full object key</param>
    /// <param name="VersionId">The version id</param>
    /// <param name="LastModifiedUtc">The UTC time the version was created</param>
    /// <param name="IsDeleteMarker">Whether the version is a delete marker</param>
    /// <param name="Size">The size of the version, or -1 if unknown</param>
    /// <param name="StorageClass">The storage class of the version, or <c>null</c> if unknown</param>
    internal readonly record struct ObjectVersion(
        string Key,
        string VersionId,
        DateTime LastModifiedUtc,
        bool IsDeleteMarker,
        long Size,
        string? StorageClass
    );

    /// <summary>
    /// Picks, for each object in a version listing, the version that was current at a point in time
    /// </summary>
    internal static class PointInTimeVersionSelector
    {
        /// <summary>
        /// Yields the version current at <paramref name="atUtc"/> for each key in the listing
        /// </summary>
        /// <remarks>
        /// The listing must be ordered as S3 returns it: by key, and newest first within a key.
        /// The first version of a key that is not newer than the point in time is the one that was
        /// current then. A key is skipped when that version is a delete marker, or when every
        /// version is newer than the point in time. The decision is kept per key across page
        /// boundaries, so a key whose versions span two pages is decided once.
        /// </remarks>
        /// <param name="versions">The version listing</param>
        /// <param name="atUtc">The UTC point in time</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The versions current at the point in time</returns>
        public static async IAsyncEnumerable<ObjectVersion> SelectCurrentAt(IAsyncEnumerable<ObjectVersion> versions, DateTime atUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string? currentKey = null;
            var decided = false;

            await foreach (var version in versions.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (version.Key != currentKey)
                {
                    currentKey = version.Key;
                    decided = false;
                }

                if (decided || version.LastModifiedUtc > atUtc)
                    continue;

                decided = true;
                if (!version.IsDeleteMarker)
                    yield return version;
            }
        }
    }
}
