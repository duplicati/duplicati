//  Copyright (C) 2015, The Duplicati Team
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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    public class SizeOmittingBackend : IBackend, IStreamingBackend
    {
        static SizeOmittingBackend() { WrappedBackend = "file"; }

        public static string WrappedBackend { get; set; }

        private IStreamingBackend m_backend;
        public SizeOmittingBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public SizeOmittingBackend(string url, Dictionary<string, string> options)
        {
            var u = new Library.Utility.Uri(url).SetScheme(WrappedBackend).ToString();
            m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(u, options);
        }

        #region IStreamingBackend implementation
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            return m_backend.PutAsync(remotename, stream, cancelToken);
        }
        public void Get(string remotename, Stream stream)
        {
            m_backend.Get(remotename, stream);
        }
        #endregion

        #region IBackend implementation
        public IEnumerable<IFileEntry> List()
        {
            return
                from n in m_backend.List()
                where !n.IsFolder
                select new FileEntry(n.Name);
        }
        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            return m_backend.PutAsync(remotename, filename, cancelToken);
        }
        public void Get(string remotename, string filename)
        {
            m_backend.Get(remotename, filename);
        }
        public void Delete(string remotename)
        {
            m_backend.Delete(remotename);
        }
        public void Test()
        {
            m_backend.Test();
        }
        public void CreateFolder()
        {
            m_backend.CreateFolder();
        }
        public string[] DNSName
        {
            get
            {
                return m_backend.DNSName;
            }
        }
        public string DisplayName
        {
            get
            {
                return "Size Omitting Backend";
            }
        }
        public string ProtocolKey
        {
            get
            {
                return "omitsize";
            }
        }
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                if (m_backend == null)
                    try { return Duplicati.Library.DynamicLoader.BackendLoader.GetSupportedCommands(WrappedBackend + "://"); }
                catch { }

                return m_backend.SupportedCommands;
            }
        }
        public string Description
        {
            get
            {
                return "A testing backend that does not return size information";
            }
        }
        #endregion
        #region IDisposable implementation
        public void Dispose()
        {
            if (m_backend != null)
                try { m_backend.Dispose(); }
            finally { m_backend = null; }
        }
        #endregion
    }
}

