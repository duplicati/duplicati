//  Copyright (C) 2019, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.CommandLine.SubProcess
{
    /// <summary>
    /// The shared interface for the dynamic loader proxy
    /// </summary>
    public interface IBackendProxy : IDisposable
    {
        /// <summary>
        /// Gets a value indicating if the backend supports streams
        /// </summary>
        /// <returns><c>true</c> if the backend supports streaming; false otherwise.</returns>
        Task<bool> GetIsStreamingBackendAsync();

        /// <summary>
        /// Gets the backend key
        /// </summary>
        /// <returns>The backend key.</returns>
        Task<string> GetKeyAsync();

        /// <summary>
        /// Lists all remote items and returns them
        /// </summary>
        /// <returns>The remote items.</returns>
        Task<IFileEntry[]> ListAsync();
        /// <summary>
        /// Deletes a remote entry
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="name">The item to delete.</param>
        Task DeleteAsync(string name);
        /// <summary>
        /// Gets a remote file
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="name">The name of the item to read.</param>
        /// <param name="stream">The stream into which the contents are placed.</param>
        Task GetAsync(string name, IStreamProxy stream);
        /// <summary>
        /// Writes a remote file
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="name">The name of the item to write.</param>
        /// <param name="stream">The stream from where the contents are read.</param>
        Task PutAsync(string name, IStreamProxy stream);
        /// <summary>
        /// Gets the DNS names.
        /// </summary>
        /// <returns>The DNS names.</returns>
        Task<string[]> GetDNSNamesAsync();
    }


    /// <summary>
    /// A remote proxy for a backend instance
    /// </summary>
    public class BackendProxy : IBackendProxy
    {
        /// <summary>
        /// The backend instance
        /// </summary>
        private readonly IBackend m_backend;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.DynamicLoaderProxy.BackendProxy"/> class.
        /// </summary>
        /// <param name="url">The url to create the instance for</param>
        /// <param name="options">The options to pass to the instance constructor</param>
        public BackendProxy(string url, Dictionary<string, string> options)
        {
            m_backend =
                Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(url, options)
                ?? throw new ArgumentException($"No such backend: {url}");
        }

        // NOTE: Even though these methods are implemented non-async,
        // we return Task's as that enables the automatic proxy
        // to wrap the communication delay in a Task as well

        /// <summary>
        /// Deletes a remote entry
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="name">The item to delete.</param>
        public Task DeleteAsync(string name)
        {
            m_backend.Delete(name);
            return Task.FromResult(true);
        }

        /// <summary>
        /// Releases all resource used by the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.DynamicLoaderProxy.BackendProxy"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.DynamicLoaderProxy.BackendProxy"/>. The
        /// <see cref="Dispose"/> method leaves the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.DynamicLoaderProxy.BackendProxy"/> in an unusable state.
        /// After calling <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.DynamicLoaderProxy.BackendProxy"/> so the garbage
        /// collector can reclaim the memory that the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.DynamicLoaderProxy.BackendProxy"/> was occupying.</remarks>
        public void Dispose()
        {
            m_backend.Dispose();
        }

        /// <summary>
        /// Gets a remote file
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="name">The name of the item to read.</param>
        /// <param name="stream">The stream into which the contents are placed.</param>
        public async Task GetAsync(string name, IStreamProxy stream)
        {
            // Pretend we have a local stream
            using (var sp = new ProxyStreamMapper(stream))
            {
                // For streaming, we can just use our stream wrapper
                if (m_backend is IStreamingBackend isb)
                {
                    isb.Get(name, sp);
                }
                else
                {
                    // Download the remote stream to a local file
                    using (var tf = new Library.Utility.TempFile())
                    {
                        m_backend.Get(name, tf);

                        // Then copy the file into the stream interface
                        using (var fs = new FileStream(tf, FileMode.Open, FileAccess.Read, FileShare.Read))
                            await fs.CopyToAsync(sp);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the backend key
        /// </summary>
        /// <returns>The backend key.</returns>
        public Task<string> GetKeyAsync()
        {
            return Task.FromResult(m_backend.ProtocolKey);
        }

        /// <summary>
        /// Lists all remote items and returns them
        /// </summary>
        /// <returns>The remote items.</returns>
        public Task<IFileEntry[]> ListAsync()
        {
            return Task.FromResult(
                m_backend
                    .List()
                    .ToArray()
            );
        }

        /// <summary>
        /// Writes a remote file
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="name">The name of the item to write.</param>
        /// <param name="stream">The stream from where the contents are read.</param>
        public async Task PutAsync(string name, IStreamProxy stream)
        {
            // Pretend we have a local stream
            using (var sp = new ProxyStreamMapper(stream))
            {
                // For streaming, we can just use our stream wrapper
                if (m_backend is IStreamingBackend isb)
                {
                    isb.Put(name, sp);
                }
                else
                {
                    // Download the stream into a local file
                    using (var tf = new Library.Utility.TempFile())
                    {
                        // Copy the stream wrapper contents into the local file
                        using (var fs = new FileStream(tf, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            await sp.CopyToAsync(fs);

                        // Upload the local file
                        m_backend.Put(name, tf);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the DNS names.
        /// </summary>
        /// <returns>The DNS names.</returns>
        public Task<string[]> GetDNSNamesAsync()
        {
            return Task.FromResult(m_backend.DNSName);
        }

        /// <summary>
        /// Gets a value indicating if the backend supports streams
        /// </summary>
        /// <returns><c>true</c> if the backend supports streaming; false otherwise.</returns>
        public Task<bool> GetIsStreamingBackendAsync()
        {
            return Task.FromResult(m_backend is IStreamingBackend);
        }
    }
}
