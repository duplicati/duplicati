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

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface a streaming backend may implement if it can open a remote file
    /// for reading from an offset, and can tell whether the remote file has changed
    /// since it was first opened. A reader that is interrupted part way through
    /// a download can then continue from where it stopped, instead of downloading
    /// the file again from the start.
    /// </summary>
    public interface IResumeEnabledBackend : IStreamingBackend
    {
        /// <summary>
        /// Opens a remote file for reading from <paramref name="offset"/>.
        /// The first call passes offset 0 and no <paramref name="etag"/>, and receives
        /// a token identifying the version of the remote file, such as an HTTP ETag.
        /// A later call passes that token back, and the backend must fail with
        /// <see cref="RemoteFileChangedException"/> if the remote file is no longer that version.
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="offset">The number of bytes to skip from the start of the remote file</param>
        /// <param name="etag">The version token returned by an earlier call, or null on the first call</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>The stream with the remote data from <paramref name="offset"/> onwards, and the version token of the remote file</returns>
        /// <exception cref="RemoteFileChangedException">The remote file is no longer the version identified by <paramref name="etag"/></exception>
        Task<(Stream Stream, string ETag)> GetResumableStreamAsync(string remotename, long offset, string? etag, CancellationToken cancelToken);
    }

    /// <summary>
    /// Thrown when a remote file is found to have changed while it was being read
    /// </summary>
    public class RemoteFileChangedException : IOException
    {
        /// <summary>
        /// Creates a new instance of the <see cref="RemoteFileChangedException"/> class
        /// </summary>
        /// <param name="message">The message describing the change</param>
        public RemoteFileChangedException(string message)
            : base(message)
        {
        }
    }
}
