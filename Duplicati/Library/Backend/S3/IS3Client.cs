// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public interface IS3Client : IDisposable
    {
        /// <summary>
        /// List all objects in a bucket
        /// </summary>
        /// <param name="bucketName">The bucket name</param>
        /// <param name="prefix">The prefix to filter the objects</param>
        /// <param name="recursive">Whether to list recursively</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The list of objects</returns>
        IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, CancellationToken cancellationToken);

        /// <summary>
        /// Add a new bucket
        /// </summary>
        /// <param name="bucketName">The name of the bucket to create</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>A task representing the asynchronous operation</returns>
        Task AddBucketAsync(string bucketName, CancellationToken cancelToken);

        /// <summary>
        /// Delete an object from a bucket
        /// </summary>
        /// <param name="bucketName">The name of the bucket</param>
        /// <param name="keyName">The name of the object to delete</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>>A task representing the asynchronous operation</returns>
        Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancelToken);

        /// <summary>
        /// Rename an object in a bucket
        /// </summary>
        /// <param name="bucketName">The name of the bucket</param>
        /// <param name="source">The source object name</param>
        /// <param name="target">The target object name</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>>A task representing the asynchronous operation</returns>
        Task RenameFileAsync(string bucketName, string source, string target, CancellationToken cancelToken);

        /// <summary>
        /// Copies the object contents into the target stream
        /// </summary>
        /// <param name="bucketName">The name of the bucket</param>
        /// <param name="keyName">The name of the object to copy</param>
        /// <param name="target">The target stream to copy the object contents into</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>>A task representing the asynchronous operation</returns>
        Task GetFileStreamAsync(string bucketName, string keyName, Stream target, CancellationToken cancelToken);

        /// <summary>
        /// Gets the DNS hostnames used for the S3 client
        /// </summary>
        /// <returns>The DNS hostnames used for the S3 client</returns>
        string? GetDnsHost();

        /// <summary>
        /// Adds a file stream to the bucket
        /// </summary>
        /// <param name="bucketName">The name of the bucket</param>
        /// <param name="keyName">The name of the object to create</param>
        /// <param name="source">The source stream to upload</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>>A task representing the asynchronous operation</returns>
        Task AddFileStreamAsync(string bucketName, string keyName, Stream source, CancellationToken cancelToken);

        /// <summary>
        /// Gets the current object lock expiration timestamp for a remote file, if available.
        /// </summary>
        /// <param name="bucketName">The bucket containing the object.</param>
        /// <param name="keyName">The full object key.</param>
        /// <param name="cancelToken">The cancellation token.</param>
        /// <returns>The UTC expiration timestamp if present, otherwise <c>null</c>.</returns>
        Task<DateTime?> GetObjectLockUntilAsync(string bucketName, string keyName, CancellationToken cancelToken);

        /// <summary>
        /// Applies or updates the object lock expiration for a remote file.
        /// </summary>
        /// <param name="bucketName">The bucket containing the object.</param>
        /// <param name="keyName">The full object key.</param>
        /// <param name="lockUntilUtc">The UTC timestamp until which the object should remain locked.</param>
        /// <param name="cancelToken">The cancellation token.</param>
        Task SetObjectLockUntilAsync(string bucketName, string keyName, DateTime lockUntilUtc, CancellationToken cancelToken);
    }
}