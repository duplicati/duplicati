using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    internal class CleanupHandler : IDisposable
    {
        private string m_backendurl;
        private FhOptions m_options;
        private RestoreStatistics m_rs;

        public CleanupHandler(string backend, FhOptions options, RestoreStatistics rs)
        {
            m_backendurl = backend;
            m_options = options;
            m_rs = rs;
        }

        public void Run()
        {
            throw new MissingMethodException();
        }

        public void Dispose()
        {
        }
    }
}
