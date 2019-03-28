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
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using LeanIPC;

namespace Duplicati.CommandLine.SubProcess
{
    public class RPCServer : RPCPeer, IDisposable
    {
        public System.Diagnostics.Process Child { get; set; }

        private IDisposable m_logProxy;

        public RPCServer(Stream reader, Stream writer)
            : base(reader, writer)
        {
        }

        public Task<IBackendProxy> CreateInstance(string url, Dictionary<string, string> options, bool isStreaming)
        {
            return CreateRemoteInstanceAsync<IBackendProxy>(typeof(BackendProxy), url, options);
        }

        public async Task<IDisposable> SetupLogging()
        {
            if (m_logProxy != null)
                throw new ArgumentException("Cannot start logging twice");

            return m_logProxy = await CreateRemoteInstanceAsync<ILogProxy>(typeof(LogProxy), new ServerLogDestinationProxy());
        }

        public void TestLogging(string message)
        {
            ((ILogProxy)m_logProxy).WriteLogMessage(message);
        }

        public override async Task StopAsync()
        {
            await base.StopAsync();
            Child?.Kill();
        }

        protected override void Dispose(bool disposing)
        {
            m_logProxy?.Dispose();
            //LoaderProxy?.Dispose();
            base.Dispose(disposing);
        }
    }
}
