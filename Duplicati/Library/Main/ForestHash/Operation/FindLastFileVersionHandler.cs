using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Operation
{
    internal class FindLastFileVersionHandler : IDisposable
    {
        private string m_backendurl;
        private FhOptions m_options;
        private RestoreStatistics m_rs;

        public FindLastFileVersionHandler(string backend, FhOptions options, RestoreStatistics rs)
        {
            m_backendurl = backend;
            m_options = options;
            m_rs = rs;
        }

        public List<KeyValuePair<string, DateTime>> Run()
        {
            throw new MissingMethodException();
        }

        public void Dispose()
        {
        }
    }
}
