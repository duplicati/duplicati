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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// The interface all backends must implement.
    /// The classes that implements this interface MUST also 
    /// implement a default constructor and a constructor that
    /// has the signature new(string url, Dictionary&lt;string, string&gt; options).
    /// The default constructor is used to construct an instance
    /// so the DisplayName and other values can be read.
    /// The other constructor is used to do the actual work.
    /// An instance is never reused.
    /// </summary>
    public interface IBackend : IDynamicModule, IDisposable
    {
        /// <summary>
        /// The localized name to display for this backend
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// The protocol key, e.g. ftp, http or ssh
        /// </summary>
        string ProtocolKey { get; }

        /// <summary>
        /// Enumerates a list of files found on the remote location
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The list of files</returns>
        IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Puts the content of the file to the url passed
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="filename">The local filename</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        Task PutAsync(string remotename, string filename, CancellationToken cancellationToken);

        /// <summary>
        /// Downloads a file with the remote data
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="filename">The local filename</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        Task GetAsync(string remotename, string filename, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes the specified file
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        Task DeleteAsync(string remotename, CancellationToken cancellationToken);

        /// <summary>
        /// A localized description of the backend, for display in the usage information
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The DNS names used to resolve the IP addresses for this backend
        /// </summary>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>The DNS names</returns>
        Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken);

        /// <summary>
        /// The purpose of this method is to test the connection to the remote backend.
        /// If any problem is encountered, this method should throw an exception.
        /// If the encountered problem is a missing target &quot;folder&quot;,
        /// this method should throw a <see cref="FolderMissingException"/>.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        Task TestAsync(CancellationToken cancellationToken);

        /// <summary>
        /// The purpose of this method is to create the underlying &quot;folder&quot;.
        /// This method will be invoked if the <see cref="Test"/> method throws a
        /// <see cref="FolderMissingException"/>. 
        /// Backends that have no &quot;folder&quot; concept should not throw
        /// a <see cref="FolderMissingException"/> during <see cref="Test"/>, 
        /// and this method should throw a <see cref="MissingMethodException"/>.
        /// </summary>
        Task CreateFolderAsync(CancellationToken cancellationToken);
    }
}
