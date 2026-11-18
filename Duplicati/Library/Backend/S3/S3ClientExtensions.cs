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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Operations that are the same for every <see cref="IS3Client"/>, built on the calls a client has
    /// </summary>
    public static class S3ClientExtensions
    {
        /// <summary>
        /// Deletes a folder and everything below it. A bucket has no folders: a folder is every
        /// object whose key starts with the folder key and a separator, plus the object some
        /// tools create under that prefix itself to stand for the folder. The objects are
        /// deleted one by one, which every S3 compatible server supports.
        /// </summary>
        /// <param name="client">The client to use</param>
        /// <param name="bucketName">The bucket name</param>
        /// <param name="folderKey">The key of the folder, with or without a trailing separator</param>
        /// <param name="cancellationToken">The cancellation token</param>
        public static async Task DeleteFolderAsync(this IS3Client client, string bucketName, string folderKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(folderKey.Trim('/')))
                throw new ArgumentException("Refusing to delete every object in the bucket", nameof(folderKey));

            var prefix = Util.AppendDirSeparator(folderKey, "/");

            // A recursive listing has every object below the prefix, at any depth, and the
            // objects that stand for the sub-folders. A listing continues after the last key
            // it returned, so deleting the keys as they are listed does not disturb it.
            await foreach (var entry in client.ListBucketAsync(bucketName, prefix, true, cancellationToken).ConfigureAwait(false))
                if (!string.IsNullOrEmpty(entry.Name))
                    await DeleteIfExistsAsync(client, bucketName, prefix + entry.Name, cancellationToken).ConfigureAwait(false);

            // The object that stands for the folder itself is not part of its listing
            await DeleteIfExistsAsync(client, bucketName, prefix, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes an object, and accepts that it is already gone
        /// </summary>
        /// <param name="client">The client to use</param>
        /// <param name="bucketName">The bucket name</param>
        /// <param name="keyName">The key of the object</param>
        /// <param name="cancellationToken">The cancellation token</param>
        private static async Task DeleteIfExistsAsync(IS3Client client, string bucketName, string keyName, CancellationToken cancellationToken)
        {
            try
            {
                await client.DeleteObjectAsync(bucketName, keyName, cancellationToken).ConfigureAwait(false);
            }
            catch (FileMissingException)
            {
                // S3 answers a delete of a missing key as a success, but not every server does
            }
        }
    }
}
