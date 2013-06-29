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

namespace Duplicati.Library.Utility
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

    /// <summary>
    /// Represents a filter that can comprise multiple filter strings
    /// </summary>    
    public class FilterExpression : IFilter
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
                else
                {
                    this.Type = (filter.Contains("*") || filter.Contains("?")) ? FilterType.Wildcard : FilterType.Simple;
                    this.Filter = filter;
                    this.Regexp = new System.Text.RegularExpressions.Regex(Library.Utility.Utility.ConvertGlobbingToRegExp(filter), REGEXP_OPTIONS);
                }
            }
            
            /// <summary>
            /// Gets a value indicating if the filter matches the path
            /// </summary>
            /// <param name="path">The path to match</param>
            public bool Matches(string path)
            {
                switch (this.Type)
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
        /// Gets the result returned if an entry matches
        /// </summary>        
        public readonly bool Result;
        
        /// <summary>
        /// Gets a value indicating whether this <see cref="Duplicati.Library.Utility.FilterExpression"/> is empty.
        /// </summary>
        /// <value><c>true</c> if empty; otherwise, <c>false</c>.</value>
        public bool Empty { get { return this.Type == FilterType.Empty; } }
        
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
        public bool Matches(string path, out bool result)
        {
            result = false;
            if (this.Type == FilterType.Empty)
                return false;
            
            if (m_filters.Where(x => x.Matches(path)).Any())
            {
                result = this.Result;
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Creates a new <see cref="Duplicati.Library.Utility.FilterExpression"/> instance, representing an empty filter.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        public FilterExpression()
            : this((IEnumerable<string>)null, true)
        {
        }
    
        /// <summary>
        /// Creates a new <see cref="Duplicati.Library.Utility.FilterExpression"/> instance.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        public FilterExpression(string filter, bool result = true)
            : this(new string[] { filter }, result)
        {
        }
    
        /// <summary>
        /// Creates a new <see cref="Duplicati.Library.Main.FilterExpression"/> instance.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        public FilterExpression(IEnumerable<string> filter, bool result = true)
        {
            this.Result = result;
            
            if (filter == null)
            {
                this.Type = FilterType.Empty;
                return;
            }
            
            m_filters = 
                (from n in filter
                let nx = new FilterEntry(n)
                where nx.Type != FilterType.Empty
                select nx).ToList();
            
            if (m_filters.Count == 0)
                this.Type = FilterType.Empty;
            else
                this.Type = (FilterType)m_filters.Max((a) => a.Type);
        }
        
        /// <summary>
        /// Utility function to match a filter with a default fall-through value
        /// </summary>
        /// <param name="filter">The filter to evaluate</param>
        /// <param name="path">The path to evaluate</param>
        /// <param name="default">The default return value if no filter matches</param>
        public static bool Matches(IFilter filter, string path, bool @default)
        {
            if (filter == null || filter.Empty)
                return @default;
        
            bool result;
            if (!filter.Matches(path, out result))
                result = @default;
                
            return result;
        }
        
        /// <summary>
        /// Combine the specified filter expressions.
        /// </summary>
        /// <param name="first">First.</param>
        /// <param name="second">Second.</param>
        public static FilterExpression Combine(FilterExpression first, FilterExpression second)
        {
            return new FilterExpression(first.m_filters.Union(second.m_filters).Select(x => x.Type == FilterType.Regexp ? ("[" + x.Filter + "]") : x.Filter), first.Result);
        }
    }
}

