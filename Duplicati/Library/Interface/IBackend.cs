#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.IO;
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
    public interface IBackend : IDisposable
    {
        /// <summary>
        /// The localized name to display for this backend
        /// </summary>
        string DisplayName { get;}

        /// <summary>
        /// The protocol key, eg. ftp, http or ssh
        /// </summary>
        string ProtocolKey { get; }

        /// <summary>
        /// A flag indicating if the backend supports streaming data, or requires a local file target/source
        /// </summary>
        bool SupportsStreaming { get; }

        /// <summary>
        /// Enumerates a list of files found on the remote location
        /// </summary>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>The list of files</returns>
        Task<IList<IFileEntry>> ListAsync(CancellationToken cancelToken);

        /// <summary>
        /// Puts the content of the file to the url passed
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="source">The stream to read data from</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>An awaitable task</returns>
        Task PutAsync(string remotename, Stream source, CancellationToken cancelToken);

        /// <summary>
        /// Downloads a file with the remote data
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="destination">The stream to write data into</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>An awaitable task</returns>
        Task GetAsync(string remotename, Stream destination, CancellationToken cancelToken);

        /// <summary>
        /// Deletes the specified file
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>An awaitable task</returns>
        Task DeleteAsync(string remotename, CancellationToken cancelToken);

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        IList<ICommandLineArgument> SupportedCommands { get; }

        /// <summary>
        /// A localized description of the backend, for display in the usage information
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The DNS names used to resolve the IP addresses for this backend
        /// </summary>
        string[] DNSName { get; }

        /// <summary>
        /// The purpose of this method is to test the connection to the remote backend.
        /// If any problem is encountered, this method should throw an exception.
        /// If the encountered problem is a missing target &quot;folder&quot;,
        /// this method should throw a <see cref="FolderMissingException"/>.
        /// </summary>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>An awaitable task</returns>
        Task TestAsync(CancellationToken cancelToken);

        /// <summary>
        /// The purpose of this method is to create the underlying &quot;folder&quot;.
        /// This method will be invoked if the <see cref="Test"/> method throws a
        /// <see cref="FolderMissingException"/>. 
        /// Backends that have no &quot;folder&quot; concept should not throw
        /// a <see cref="FolderMissingException"/> during <see cref="Test"/>, 
        /// and this method should throw a <see cref="MissingMethodException"/>.
        /// </summary>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <returns>An awaitable task</returns>
        Task CreateFolderAsync(CancellationToken cancelToken);
    }
}
