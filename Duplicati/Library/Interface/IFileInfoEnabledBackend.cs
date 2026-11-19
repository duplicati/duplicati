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

using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface a backend may implement if it can look up a single remote file,
    /// such as with an HTTP HEAD request, without listing the whole remote folder.
    /// </summary>
    public interface IFileInfoEnabledBackend : IBackend
    {
        /// <summary>
        /// Gets the metadata of a single remote file
        /// </summary>
        /// <param name="remotename">The remote filename, as passed to <see cref="IBackend.PutAsync(string, string, CancellationToken)"/></param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>The file entry, or null when the file does not exist.
        /// The size is negative when the backend does not know it.</returns>
        /// <remarks>
        /// Null means the file does not exist, and fails the upload verification,
        /// so a backend that cannot tell must throw instead of returning null.
        /// </remarks>
        Task<IFileEntry?> GetFileInfoAsync(string remotename, CancellationToken cancelToken);
    }
}
