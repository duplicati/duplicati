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

using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface a backend may implement if it supports streaming operations.
    /// Backends that implement this interface can be throttled and correctly shows 
    /// the progressbar when transferring data.
    /// </summary>
    public interface IStreamingBackend : IBackend
    {
        /// <summary>
        /// Puts the content of the file to the url passed
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="stream">The stream to read from</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken);

        /// <summary>
        /// Downloads a file with the remote data
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="stream">The stream to write data to</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken);

    }
}
