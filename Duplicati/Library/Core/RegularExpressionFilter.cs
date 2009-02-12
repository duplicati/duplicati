using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Duplicati.Library.Core
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
            //TODO: This should probably be determined by filesystem rather than OS
            //In case MS decides to support case sensitive filesystems (yeah right :))
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX)
                opts |= RegexOptions.IgnoreCase;

            m_expression = new Regex(expression, opts);
            m_include = include;
        }

        public RegularExpressionFilter(bool include, Regex expression)
        {
            m_expression = expression;
            m_include = include;
        }

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
