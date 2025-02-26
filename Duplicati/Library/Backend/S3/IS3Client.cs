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
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public interface IS3Client : IDisposable
    {
        IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, CancellationToken cancellationToken);

        Task AddBucketAsync(string bucketName, CancellationToken cancelToken);

        Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancelToken);

        Task RenameFileAsync(string bucketName, string source, string target, CancellationToken cancelToken);

        Task GetFileStreamAsync(string bucketName, string keyName, System.IO.Stream target, CancellationToken cancelToken);

        string GetDnsHost();

        Task AddFileStreamAsync(string bucketName, string keyName, System.IO.Stream source, CancellationToken cancelToken);
    }
}