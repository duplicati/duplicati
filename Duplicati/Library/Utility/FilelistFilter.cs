#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
using System.Text;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// A simple filter that includes or excludes files, based on a list
    /// </summary>
    public class FilelistFilter : IFilenameFilter
    {
        private Dictionary<string, string> m_filter = new Dictionary<string, string>(Utility.IsFSCaseSensitive ? StringComparer.CurrentCulture : StringComparer.CurrentCultureIgnoreCase);
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
