//  Copyright (C) 2013, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Describes the different complexities of file lists
    ///</summary>
    public enum FilterType : int
    {
        /// <summary>
        /// No filter expression
        /// </summary>
        Empty,
        /// <summary>
        /// A simple list of names
        /// </summary>
        Simple,
        /// <summary>
        /// A list of files described with wildcards
        /// </summary>
        Wildcard,
        /// <summary>
        /// A list of files described with regular expressions
        /// </summary>
        Regexp
    }
        
    public class FilterExpression
    {   
        private struct FilterEntry
        {
            public readonly FilterType Type;
            public readonly string Filter;
            public readonly System.Text.RegularExpressions.Regex Regexp;
            
            private static readonly System.Text.RegularExpressions.RegexOptions REGEXP_OPTIONS = Library.Utility.Utility.IsFSCaseSensitive ? System.Text.RegularExpressions.RegexOptions.Compiled : System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            
            public FilterEntry(string filter)
            {
                if (string.IsNullOrEmpty(filter))
                {
                    this.Type = FilterType.Empty;
                    this.Filter = null;
                    this.Regexp = null;
                }
                if (filter.StartsWith("[") && filter.EndsWith("]"))
                {
                    this.Type = FilterType.Regexp;
                    this.Filter = filter.Substring(1, filter.Length - 2);
                    this.Regexp = new System.Text.RegularExpressions.Regex(this.Filter, REGEXP_OPTIONS);
                }
                else if (filter.Contains("*") || filter.Contains("?"))
                {
                    this.Type = FilterType.Wildcard;
                    this.Filter = filter;
                    this.Regexp = new System.Text.RegularExpressions.Regex(Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(filter), REGEXP_OPTIONS);
                }
                else
                {
                    this.Type = FilterType.Simple;
                    this.Filter = filter;
                    this.Regexp = new System.Text.RegularExpressions.Regex(Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(filter), REGEXP_OPTIONS);
                }
            }
            
            /// <summary>
            /// Gets a value indicating if the filter matches the path
            /// </summary>
            /// <param name="path">The path to match</param>
            public bool Matches(string path)
            {
                switch(this.Type)
                {
                    case FilterType.Simple:
                        return string.Equals(this.Filter, path, Library.Utility.Utility.ClientFilenameStringComparision);
                    case FilterType.Wildcard:
                    case FilterType.Regexp:
                        var m = this.Regexp.Match(path);
                        return m.Success && m.Length == path.Length;
                    default:
                        return false;
                }
            }
        }
    
        /// <summary>
        /// The internal list of expressions
        /// </summary>
        private List<FilterEntry> m_filters;
    
        /// <summary>
        /// Gets the type of the filter
        /// </summary>
        public readonly FilterType Type;
        
        /// <summary>
        /// Gets the simple list, if the type is simple, named or wildcard
        /// </summary>
        /// <returns>The simple list</returns>
        public string[] GetSimpleList()
        {
            if (this.Type == FilterType.Simple || this.Type == FilterType.Wildcard)
                return (from n in m_filters select n.Filter).ToArray();
            else
                throw new InvalidOperationException(string.Format("Cannot extract simple list when the type is: {0}", this.Type));
        }
        
        /// <summary>
        /// Gets a value indicating if the filter matches the path
        /// </summary>
        /// <param name="path">The path to match</param>
        public bool Matches(string path)
        {
            if (this.Type == FilterType.Empty)
                return false;
                
            return m_filters.Where(x => x.Matches(path)).Any();
        }
    
        /// <summary>
        /// Creates a new  <see cref="Duplicati.Library.Main.FilterExpression"/> class.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        public FilterExpression(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                this.Type = FilterType.Empty;
                return;
            }
            
            var subqueries = filter.Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries);
            if (subqueries.Length == 0)
            {
                this.Type = FilterType.Empty;
                return;
            }
            
            m_filters = 
                (from n in subqueries
                let nx = new FilterEntry(n)
                where nx.Type != FilterType.Empty
                select nx).ToList();
            
            if (m_filters.Count == 0)
                this.Type = FilterType.Empty;
            else
                this.Type = (FilterType)m_filters.Max((a) => a.Type);
        }
    }
}

