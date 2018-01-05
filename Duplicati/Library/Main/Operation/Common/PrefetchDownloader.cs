//  Copyright (C) 2017, The Duplicati Team
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
using CoCoL;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation.Common
{
    internal class PrefetchDownloader : IDisposable
    {
        private readonly IWriteChannelEnd<IAsyncDownloadedFile> m_source;
        private readonly IReadChannelEnd<IAsyncDownloadedFile> m_result;

        private class AsyncDownloadedFile : IAsyncDownloadedFile
        {
            public Library.Utility.TempFile TempFile { get; set;  }
			public string Name { get; set; }
            public string Hash { get; set; }
            public long Size { get; set; }
        }

        public PrefetchDownloader(IEnumerable<IRemoteVolume> volumes, BackendHandler backend, int volumesahead = 1)
        {
            var channel = ChannelManager.CreateChannel<IAsyncDownloadedFile>(buffersize: volumesahead);
            m_source = channel.AsWriteOnly();
            m_result = channel.AsReadOnly();

            Start(volumes, backend);
		}

        private void Start(IEnumerable<IRemoteVolume> volumes, BackendHandler backend)
        {
            AutomationExtensions.RunTask(
                m_source,
                async _ =>
                {
                    foreach(var n in volumes)
                    {
                        // Prepare to dispose it
                        using (var tf = await backend.GetFileAsync(n.Name, n.Size, n.Hash))
                        {
                            await m_source.WriteAsync(new AsyncDownloadedFile()
                            {
                                TempFile = Library.Utility.TempFile.WrapExistingFile(tf),
                                Name = n.Name,
                                Hash = n.Hash,
                                Size = n.Size
                            });

                            // If we sent it on, then do not delete it
                            tf.Protected = true;
                        }
                    }

                    await m_source.WriteAsync(null);
                }
            );
        }

        public Task<IAsyncDownloadedFile> GetNextAsync()
        {
            return m_result.ReadAsync();
        }

        public Task StopAsync()
        {
            m_source.Dispose();

            return AutomationExtensions.RunTask(
                m_result,
                async _ =>
                {
                    while (true)
                    {
                        var tmp = await m_result.ReadAsync();
                        if (tmp != null && tmp.TempFile != null)
                            tmp.TempFile.Dispose();
                    }
				}
            );
            
        }

        public void Dispose()
        {
            m_source.Dispose();
        }
    }
}
