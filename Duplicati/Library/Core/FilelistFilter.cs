using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Core
{
    /// <summary>
    /// A simple filter that includes or excludes files, based on a list
    /// </summary>
    public class FilelistFilter : IFilenameFilter
    {
        private Dictionary<string, string> m_filter = new Dictionary<string, string>(System.Environment.OSVersion.Platform == PlatformID.MacOSX || System.Environment.OSVersion.Platform == PlatformID.Unix ? StringComparer.CurrentCulture : StringComparer.CurrentCultureIgnoreCase);
        private bool m_include;

        public FilelistFilter(bool include, IEnumerable<string> filenames)
        {
            m_include = include;
            foreach (string s in filenames)
                if (!System.IO.Path.IsPathRooted(s))
                    m_filter[System.IO.Path.DirectorySeparatorChar + s] = null;
                else
                    m_filter[s] = null;
        }

        #region IFilenameFilter Members

        public bool Match(string filename)
        {
            return m_filter.ContainsKey(filename);
        }

        public bool Include { get { return m_include; } }

        #endregion
    }
}
