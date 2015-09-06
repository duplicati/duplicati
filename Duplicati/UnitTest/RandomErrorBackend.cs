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
using System;using Duplicati.Library.Interface;using System.Collections.Generic;using System.IO;

namespace Duplicati.UnitTest
{
    public class RandomErrorBackend : IBackend, IStreamingBackend
    {        static RandomErrorBackend() { WrappedBackend = "file"; }        private static Random random = new Random(42);        public static string WrappedBackend { get; set; }        private IStreamingBackend m_backend;
        public RandomErrorBackend()
        {
        }        public RandomErrorBackend(string url, Dictionary<string, string> options)        {            var u = new Library.Utility.Uri(url).SetScheme(WrappedBackend).ToString();            m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(u, options);        }        private void ThrowErrorRandom()        {            if (random.NextDouble() > 0.90)                throw new Exception("Random upload failure");        }
        #region IStreamingBackend implementation        public void Put(string remotename, Stream stream)        {            var uploadError = random.NextDouble() > 0.9;            using(var f = new Library.Utility.ProgressReportingStream(stream, stream.Length, x => { if (uploadError && stream.Position > stream.Length / 2) throw new Exception("Random upload failure"); }))                m_backend.Put(remotename, f);            ThrowErrorRandom();        }        public void Get(string remotename, Stream stream)        {            ThrowErrorRandom();            m_backend.Get(remotename, stream);            ThrowErrorRandom();        }        #endregion        #region IBackend implementation        public List<IFileEntry> List()        {            return m_backend.List();        }        public void Put(string remotename, string filename)        {            ThrowErrorRandom();            m_backend.Put(remotename, filename);            ThrowErrorRandom();        }        public void Get(string remotename, string filename)        {            ThrowErrorRandom();            m_backend.Get(remotename, filename);            ThrowErrorRandom();        }        public void Delete(string remotename)        {            ThrowErrorRandom();            m_backend.Delete(remotename);            ThrowErrorRandom();        }        public void Test()        {            m_backend.Test();        }        public void CreateFolder()        {            m_backend.CreateFolder();        }        public string DisplayName        {            get            {                return "Random Error Backend";            }        }        public string ProtocolKey        {            get            {                return "randomerror";            }        }        public IList<ICommandLineArgument> SupportedCommands        {            get            {                if (m_backend == null)                    try { return Duplicati.Library.DynamicLoader.BackendLoader.GetSupportedCommands(WrappedBackend + "://"); }                    catch { }                return m_backend.SupportedCommands;            }        }        public string Description        {            get            {                return "A testing backend that randomly fails";            }        }        #endregion        #region IDisposable implementation        public void Dispose()        {            if (m_backend != null)                try { m_backend.Dispose(); }                finally { m_backend = null; }        }        #endregion    }
}

