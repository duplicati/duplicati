using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// An interface a backend may implement if it supports streaming operations.
    /// </summary>
    public interface IStreamingBackend : IBackend
    {
        /// <summary>
        /// A value that indicates if the backend supports streaming.
        /// Backends that implement the IStreamingBackend interface should return true.
        /// </summary>
        bool SupportsStreaming { get; }

        /// <summary>
        /// Puts the content of the file to the url passed
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="stream">The stream to read from</param>
        void Put(string remotename, System.IO.Stream stream);

        /// <summary>
        /// Downloads a file with the remote data
        /// </summary>
        /// <param name="remotename">The remote filename, relative to the URL</param>
        /// <param name="stream">The stream to write data to</param>
        void Get(string remotename, System.IO.Stream stream);

    }
}
