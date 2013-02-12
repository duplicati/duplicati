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
using System.Text.RegularExpressions;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// A filename filter based on regular expressions
    /// </summary>
    public class RegularExpressionFilter : IFilenameFilter 
    {
        private Regex m_expression;
        private bool m_include;

        public RegularExpressionFilter(bool include, string expression)
        {
            RegexOptions opts = RegexOptions.Compiled;
			if (!Utility.IsFSCaseSensitive)
				opts |= RegexOptions.IgnoreCase;

            m_expression = new Regex(expression, opts);
            m_include = include;
        }

        public RegularExpressionFilter(bool include, Regex expression)
        {
            m_expression = expression;
            m_include = include;
        }

        public Regex Expression { get { return m_expression; } }

        #region IFilenameFilter Members

        public bool Include
        {
            get { return m_include; }
        }

        public bool Match(string filename)
        {
            return m_expression.Match(filename).Length == filename.Length;
        }

        #endregion
    }
}
