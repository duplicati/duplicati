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
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;

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
        Regexp,
        /// <summary>
        /// A built in set of filters
        /// </summary>
        Group,
    }

    /// <summary>
    /// Represents a filter that can comprise multiple filter strings
    /// </summary>    
    public class FilterExpression : IFilter
    {
        /// <summary>
        /// Implementation of a filter entry
        /// </summary>
        private struct FilterEntry
        {
            /// <summary>
            /// The type of the filter
            /// </summary>
            public readonly FilterType Type;
            /// <summary>
            /// The filter string
            /// </summary>
            public readonly string Filter;
            /// <summary>
            /// The regular expression version of the filter
            /// </summary>
            public readonly Regex Regexp;

            /// <summary>
            /// The single wildcard character (DOS style)
            /// </summary>
            private const char SINGLE_WILDCARD = '?';
            /// <summary>
            /// The multiple wildcard character (DOS style)
            /// </summary>
            private const char MULTIPLE_WILDCARD = '*';

            /// <summary>
            /// The regular expression flags
            /// </summary>
            private static readonly RegexOptions REGEXP_OPTIONS =
                RegexOptions.Compiled |
                RegexOptions.ExplicitCapture |
                (Utility.IsFSCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) |
                RegexOptions.Singleline;

            // Since we might need to get the regex for a particular filter group multiple times
            // (e.g., when combining multiple FilterExpressions together, which discards the existing FilterEntries and recreates them from the Filter representations),
            // and since they don't change (and compiling them isn't a super cheap operation), we keep a cache of the ones we've built for re-use.
            private static readonly Dictionary<FilterGroup, Regex> filterGroupRegexCache = new Dictionary<FilterGroup, Regex>();

            /// <summary>
            /// Initializes a new instance of the <see cref="T:Duplicati.Library.Utility.FilterExpression.FilterEntry"/> struct.
            /// </summary>
            /// <param name="filter">The filter string to use.</param>
            public FilterEntry(string filter)
            {
                if (string.IsNullOrEmpty(filter))
                {
                    this.Type = FilterType.Empty;
                    this.Filter = null;
                    this.Regexp = null;
                }
                else if (filter.StartsWith("[", StringComparison.Ordinal) && filter.EndsWith("]", StringComparison.Ordinal))
                {
                    this.Type = FilterType.Regexp;
                    this.Filter = filter.Substring(1, filter.Length - 2);
                    if (Filter.StartsWith("^") && Filter.EndsWith("$"))
                    {
                        this.Regexp = new Regex(this.Filter, REGEXP_OPTIONS);
                    }
                    else
                    {
                        this.Regexp = new Regex("^(" + this.Filter + ")$", REGEXP_OPTIONS);
                    }
                }
                else if (filter.StartsWith("{", StringComparison.Ordinal) && filter.EndsWith("}", StringComparison.Ordinal))
                {
                    this.Type = FilterType.Group;
                    this.Filter = filter.Substring(1, filter.Length - 2);
                    this.Regexp = GetFilterGroupRegex(this.Filter);
                }
                else if (filter.StartsWith("@", StringComparison.Ordinal))
                {
                    // Take filter literally; i.e., don't treat wildcard
                    // characters as globbing characters
                    this.Type = FilterType.Simple;
                    this.Filter = filter.Substring(1);
                    this.Regexp = new Regex(Utility.ConvertLiteralToRegExp(this.Filter), REGEXP_OPTIONS);
                }
                else
                {
                    this.Type = (filter.Contains(MULTIPLE_WILDCARD) || filter.Contains(SINGLE_WILDCARD)) ? FilterType.Wildcard : FilterType.Simple;
                    this.Filter = (!Utility.IsFSCaseSensitive && this.Type == FilterType.Wildcard) ? filter.ToUpper(CultureInfo.InvariantCulture) : filter;
                    this.Regexp = new Regex(Utility.ConvertGlobbingToRegExp(filter), REGEXP_OPTIONS);
                }
            }

            /// <summary>
            /// Gets the regex that represents the given filter group
            /// </summary>
            /// <param name="filterGroupName">Filter group name</param>
            /// <returns>Group regex</returns>
            private static Regex GetFilterGroupRegex(string filterGroupName)
            {
                FilterGroup filterGroup = FilterGroups.ParseFilterList(filterGroupName, FilterGroup.None);
                Regex result;
                if (FilterEntry.filterGroupRegexCache.TryGetValue(filterGroup, out result))
                {
                    return result;
                }
                else
                {
                    // Get the filter strings for this filter group, and convert them to their regex forms
                    List<string> regexStrings = FilterGroups.GetFilterStrings(filterGroup)
                    .Select(filterString =>
                    {
                        if (filterString.StartsWith("[", StringComparison.Ordinal) && filterString.EndsWith("]", StringComparison.Ordinal))
                        {
                            return filterString.Substring(1, filterString.Length - 2);
                        }
                        else if (filterString.StartsWith("@", StringComparison.Ordinal))
                        {
                            return Utility.ConvertLiteralToRegExp(filterString.Substring(1));
                        }
                        else
                        {
                            return Utility.ConvertGlobbingToRegExp(filterString);
                        }
                    })
                    .ToList();

                    string regexString;
                    if (regexStrings.Count == 1)
                    {
                        regexString = regexStrings.Single();
                    }
                    else
                    {
                        // If there are multiple regex strings, then they need to be merged by wrapping each in parenthesis and ORing them together
                        regexString = "(" + string.Join(")|(", regexStrings) + ")";
                    }

                    result = new Regex("^(" + regexString + ")$", REGEXP_OPTIONS);

                    FilterEntry.filterGroupRegexCache[filterGroup] = result;

                    return result;
                }
            }

            /// <summary>
            /// Tests whether specified string can be matched against provided pattern string. Pattern may contain single- and multiple-replacing
            /// wildcard characters.
            /// </summary>
            /// <param name="input">String which is matched against the pattern.</param>
            /// <param name="pattern">Pattern against which string is matched.</param>
            /// <returns>true if <paramref name="pattern"/> matches the string <paramref name="input"/>; otherwise false.</returns>
            /// <note>From: http://www.c-sharpcorner.com/uploadfile/b81385/efficient-string-matching-algorithm-with-use-of-wildcard-characters/</note>
            private static bool IsWildcardMatch(string input, string pattern)
            {
                Stack<int> inputPosStack = new Stack<int>(); // Stack containing input positions that should be tested for further matching
                Stack<int> patternPosStack = new Stack<int>(); // Stack containing pattern positions that should be tested for further matching

                bool[,] pointTested = new bool[input.Length + 1, pattern.Length + 1];       // Each true value indicates that input position vs. pattern position has been tested
                int inputPos = 0;   // Position in input matched up to the first multiple wildcard in pattern
                int patternPos = 0; // Position in pattern matched up to the first multiple wildcard in pattern
                // Match beginning of the string until first multiple wildcard in pattern
                while (inputPos < input.Length && patternPos < pattern.Length && pattern[patternPos] != MULTIPLE_WILDCARD && (input[inputPos] == pattern[patternPos] || pattern[patternPos] == SINGLE_WILDCARD))
                {
                    inputPos++;
                    patternPos++;
                }

                // Push this position to stack if it points to end of pattern or to a general wildcard
                if (patternPos == pattern.Length || pattern[patternPos] == MULTIPLE_WILDCARD)
                {
                    pointTested[inputPos, patternPos] = true;
                    inputPosStack.Push(inputPos);
                    patternPosStack.Push(patternPos);
                }
                bool matched = false;
                // Repeat matching until either string is matched against the pattern or no more parts remain on stack to test
                while (inputPosStack.Count > 0 && !matched)
                {
                    inputPos = inputPosStack.Pop(); // Pop input and pattern positions from stack
                    patternPos = patternPosStack.Pop(); // Matching will succeed if rest of the input string matches rest of the pattern

                    // Modified from original version to match zero or more characters
                    //if (inputPos == input.Length && patternPos == pattern.Length)

                    if (inputPos == input.Length && (patternPos == pattern.Length || (patternPos == pattern.Length - 1 && pattern[patternPos] == MULTIPLE_WILDCARD)))
                        matched = true;     // Reached end of both pattern and input string, hence matching is successful
                    else
                    {
                        // First character in next pattern block is guaranteed to be multiple wildcard
                        // So skip it and search for all matches in value string until next multiple wildcard character is reached in pattern
                        for (int curInputStart = inputPos; curInputStart < input.Length; curInputStart++)
                        {
                            int curInputPos = curInputStart;
                            int curPatternPos = patternPos + 1;
                            if (curPatternPos == pattern.Length)
                            {   // Pattern ends with multiple wildcard, hence rest of the input string is matched with that character
                                curInputPos = input.Length;
                            }
                            else
                            {
                                while (curInputPos < input.Length && curPatternPos < pattern.Length && pattern[curPatternPos] != MULTIPLE_WILDCARD &&
                                    (input[curInputPos] == pattern[curPatternPos] || pattern[curPatternPos] == SINGLE_WILDCARD))
                                {
                                    curInputPos++;
                                    curPatternPos++;
                                }
                            }
                            // If we have reached next multiple wildcard character in pattern without breaking the matching sequence, then we have another candidate for full match
                            // This candidate should be pushed to stack for further processing
                            // At the same time, pair (input position, pattern position) will be marked as tested, so that it will not be pushed to stack later again
                            if (((curPatternPos == pattern.Length && curInputPos == input.Length) || (curPatternPos < pattern.Length && pattern[curPatternPos] == MULTIPLE_WILDCARD))
                                && !pointTested[curInputPos, curPatternPos])
                            {
                                pointTested[curInputPos, curPatternPos] = true;
                                inputPosStack.Push(curInputPos);
                                patternPosStack.Push(curPatternPos);
                            }
                        }
                    }
                }
                return matched;
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
                        return string.Equals(this.Filter, path, Library.Utility.Utility.ClientFilenameStringComparison);
                    case FilterType.Wildcard:
                        return IsWildcardMatch(!Utility.IsFSCaseSensitive ? path.ToUpper(CultureInfo.InvariantCulture) : path, this.Filter);
                    case FilterType.Regexp:
                    case FilterType.Group:
                        var m = this.Regexp.Match(path);
                        return m.Success && m.Length == path.Length;
                    default:
                        return false;
                }
            }

            public override string ToString()
            {
                switch (this.Type)
                {
                    case FilterType.Regexp:
                        return "[" + this.Filter + "]";
                    case FilterType.Group:
                        return "{" + this.Filter + "}";
                    case FilterType.Simple:
                        return "@" + this.Filter;
                    default:
                        return this.Filter;
                }
            }
        }

        /// <summary>
        /// The internal list of expressions
        /// </summary>
        private readonly List<FilterEntry> m_filters;

        /// <summary>
        /// Gets the type of the filter
        /// </summary>
        public readonly FilterType Type;

        /// <summary>
        /// Gets the result returned if an entry matches
        /// </summary>        
        public readonly bool Result;

        /// <summary>
        /// Gets a value indicating whether this <see cref="FilterExpression"/> is empty.
        /// </summary>
        /// <value><c>true</c> if empty; otherwise, <c>false</c>.</value>
        public bool Empty => Type == FilterType.Empty;

        /// <summary>
        /// Gets the simple list, if the type is simple, named or wildcard
        /// </summary>
        /// <returns>The simple list</returns>
        public string[] GetSimpleList()
        {
            if (this.Type == FilterType.Simple || this.Type == FilterType.Wildcard)
                return (from n in m_filters select n.Filter).ToArray();
            else
                throw new InvalidOperationException($"Cannot extract simple list when the type is: {this.Type}");
        }

        /// <summary>
        /// Gets a value indicating if the filter matches the path
        /// </summary>
        /// <param name="result">The match result</param>
        /// <param name="match">The filter that matched</param>
        public bool Matches(string path, out bool result, out IFilter match)
        {
            result = false;
            if (this.Type == FilterType.Empty)
            {
                match = null;
                return false;
            }

            if (m_filters.Any(x => x.Matches(path)))
            {
                match = this;
                result = this.Result;
                return true;
            }

            match = null;
            return false;
        }

        /// <inheritdoc />
        public string GetFilterHash()
        {
            var hash = MD5HashHelper.GetHash(m_filters?.Select(x => x.Filter));
            return Utility.ByteArrayAsHexString(hash);
        }

        /// <summary>
        /// Creates a new <see cref="FilterExpression"/> instance, representing an empty filter.
        /// </summary>
        public FilterExpression()
            : this((IEnumerable<string>)null, true)
        {
        }

        /// <summary>
        /// Creates a new <see cref="FilterExpression"/> instance.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        public FilterExpression(string filter, bool result = true)
            : this(Expand(filter), result)
        {
        }

        /// <summary>
        /// Creates a new <see cref="FilterExpression"/> instance.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        /// <param name="result">Return value of <see cref="Matches(string,out bool,out IFilter)"/> in case of match</param>
        public FilterExpression(IEnumerable<string> filter, bool result = true)
        {
            this.Result = result;

            if (filter == null)
            {
                this.Type = FilterType.Empty;
                return;
            }

            m_filters = Compact(
                (from n in filter
                 let nx = new FilterEntry(n)
                 where nx.Type != FilterType.Empty
                 select nx)
            );

            if (m_filters.Count == 0)
                this.Type = FilterType.Empty;
            else
                this.Type = m_filters.Max((a) => a.Type);
        }

        private static IEnumerable<string> Expand(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return null;

            if (filter.Length < 2 || (filter.StartsWith("[", StringComparison.Ordinal) && filter.EndsWith("]", StringComparison.Ordinal)))
            {
                return new string[] { filter };
            }

            if (filter.StartsWith("{", StringComparison.Ordinal) && filter.EndsWith("}", StringComparison.Ordinal))
            {
                string groupName = filter.Substring(1, filter.Length - 2);
                FilterGroup filterGroup = FilterGroups.ParseFilterList(groupName, FilterGroup.None);
                return (filterGroup == FilterGroup.None) ? null : FilterGroups.GetFilterStrings(filterGroup);
            }

            return filter.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static List<FilterEntry> Compact(IEnumerable<FilterEntry> items)
        {
            var r = new List<FilterEntry>();
            System.Text.StringBuilder combined = new System.Text.StringBuilder();
            bool first = false;
            foreach (var f in items)
            {
                if (combined.Length == 0)
                {
                    // Note that even though group filters may include regexes, we don't want to merge them together and compact them,
                    // since that would make their names much more difficult to interpret on the command line.
                    if (f.Type == FilterType.Simple || f.Type == FilterType.Wildcard || f.Type == FilterType.Group)
                        r.Add(f);
                    else if (f.Type != FilterType.Empty)
                    {
                        combined.Append(f.Regexp.ToString());
                        first = true;
                    }
                }
                else
                {
                    if (f.Type == FilterType.Simple || f.Type == FilterType.Wildcard || f.Type == FilterType.Group)
                    {
                        r.Add(new FilterEntry("[" + combined.Append("]")));
                        r.Add(f);
                        combined.Clear();
                    }
                    else if (f.Type != FilterType.Empty)
                    {
                        if (first)
                        {
                            combined.Insert(0, "(").Append(")");
                            first = false;
                        }

                        combined.Append("|(" + f.Regexp + ")");
                    }
                }
            }

            if (combined.Length > 0)
                r.Add(new FilterEntry("[" + combined.Append("]")));

            return r;
        }

        /// <summary>
        /// A cache for computing the fallback strategy for a filter
        /// </summary>
        private static readonly Dictionary<IFilter, Tuple<bool, bool>> _matchFallbackLookup = new Dictionary<IFilter, Tuple<bool, bool>>();

        /// <summary>
        /// The lock object for protecting access to the lookup table
        /// </summary>
        private readonly static object _matchLock = new object();

        /// <summary>
        /// Utility function to match a filter with a default fall-through value
        /// </summary>
        /// <param name="filter">The filter to evaluate</param>
        /// <param name="path">The path to evaluate</param>
        public static bool Matches(IFilter filter, string path)
        {
            return Matches(filter, path, out _);
        }

        /// <summary>
        /// Examines a list of filters and returns flags indicating if the list contains excludes and includes
        /// </summary>
        /// <param name="filter">The filter to examine</param>
        /// <param name="includes">True if the filter contains includes, false otherwise.</param>
        /// <param name="excludes">True if the filter contains excludes, false otherwise.</param>
        public static void AnalyzeFilters(IFilter filter, out bool includes, out bool excludes)
        {
            includes = false;
            excludes = false;

            Tuple<bool, bool> cacheLookup = null;

            // Check for cached results
            if (filter != null)
                lock (_matchLock)
                    if (_matchFallbackLookup.TryGetValue(filter, out cacheLookup))
                    {
                        includes = cacheLookup.Item1;
                        excludes = cacheLookup.Item2;
                    }

            // Figure out what components are in the filter
            if (cacheLookup == null)
            {
                var q = new Queue<IFilter>();
                q.Enqueue(filter);

                while (q.Count > 0)
                {
                    var p = q.Dequeue();
                    if (p == null || p.Empty)
                        continue;
                    else if (p is FilterExpression expression)
                    {
                        if (expression.Result)
                            includes = true;
                        else
                            excludes = true;
                    }
                    else if (p is JoinedFilterExpression filterExpression)
                    {
                        q.Enqueue(filterExpression.First);
                        q.Enqueue(filterExpression.Second);
                    }
                }

                // Populate the cache
                lock (_matchLock)
                {
                    if (_matchFallbackLookup.Count > 10)
                        _matchFallbackLookup.Remove(_matchFallbackLookup.Keys.Skip(new Random().Next(0, _matchFallbackLookup.Count)).First());
                    _matchFallbackLookup[filter] = new Tuple<bool, bool>(includes, excludes);
                }
            }
        }

        /// <summary>
        /// Utility function to match a filter with a default fall-through value
        /// </summary>
        /// <param name="filter">The filter to evaluate</param>
        /// <param name="path">The path to evaluate</param>
        /// <param name="match">The filter that matched</param>
        public static bool Matches(IFilter filter, string path, out IFilter match)
        {
            if (filter == null || filter.Empty)
            {
                match = null;
                return true;
            }

            bool result;
            if (filter.Matches(path, out result, out match))
                return result;

            bool includes;
            bool excludes;

            AnalyzeFilters(filter, out includes, out excludes);
            match = null;

            // We have only include filters, we exclude files by default
            if (includes && !excludes)
            {
                return false;
            }
            // Otherwise we include by default
            else
            {
                return true;
            }

        }

        /// <summary>
        /// Combine the specified filter expressions.
        /// </summary>
        /// <param name="first">First.</param>
        /// <param name="second">Second.</param>
        public static FilterExpression Combine(FilterExpression first, FilterExpression second)
        {
            if (first == null || first.Empty)
                return second;
            if (second == null || second.Empty)
                return first;

            if (first.Result != second.Result)
                throw new ArgumentException("Both filters must have the same result property");
            return new FilterExpression(first.m_filters.Union(second.m_filters).Select(x => x.ToString()), first.Result);
        }

        /// <summary>
        /// Combine the specified filter expressions.
        /// </summary>
        /// <param name="first">First.</param>
        /// <param name="second">Second.</param>
        public static IFilter Combine(IFilter first, IFilter second)
        {
            if (second == null || second.Empty)
                return first;
            if (first == null || first.Empty)
                return second;

            if (first is FilterExpression expression && second is FilterExpression filterExpression && expression.Result == filterExpression.Result)
                return Combine(expression, filterExpression);

            return new JoinedFilterExpression(first, second);
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents the current <see cref="FilterExpression"/>.
        /// </summary>
        /// <returns>A <see cref="System.String"/> that represents the current <see cref="FilterExpression"/>.</returns>
        public override string ToString()
        {
            if (this.Empty)
                return "";

            return
                "(" +
                string.Join(") || (",
                    (from n in m_filters
                     select n.ToString())
                ) +
                ")";
        }

        /// <summary>
        /// Serializes the filter instance into a list of strings 
        /// that can be passed to the deserialize method
        /// </summary>
        public string[] Serialize()
        {
            if (this.Empty)
                return null;

            return
                (from n in m_filters
                 select $"{(this.Result ? "+" : "-")}{n.ToString()}"
                ).ToArray();
        }

        /// <summary>
        /// Serializes the filter instance into a list of strings 
        /// that can be passed to the deserialize method
        /// </summary>
        public static string[] Serialize(IFilter filter)
        {
            if (filter == null || filter.Empty)
                return new string[0];

            IEnumerable<string> res = new string[0];
            var work = new Stack<IFilter>();
            work.Push(filter);

            while (work.Count > 0)
            {
                var f = work.Pop();

                if (f is FilterExpression expression)
                    res = res.Union(expression.Serialize());
                else if (f is JoinedFilterExpression filterExpression)
                {
                    work.Push(filterExpression.Second);
                    work.Push(filterExpression.First);
                }
                else
                    throw new Exception(string.Format("Cannot serialize filter instance of type: {0}", f.GetType()));

            }
            return res.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }

        /// <summary>
        /// Builds a filter expression from a list of strings
        /// prefixed with either minus or plus
        /// </summary>
        /// <param name="filters">The filters to deserialize from.</param>
        public static IFilter Deserialize(string[] filters)
        {
            if (filters == null || filters.Length == 0)
                return null;

            IFilter res = null;
            foreach (var n in filters)
            {
                bool include;
                if (n.StartsWith("+", StringComparison.Ordinal))
                    include = true;
                else if (n.StartsWith("-", StringComparison.Ordinal))
                    include = false;
                else
                    continue;

                res = Combine(res, new FilterExpression(n.Substring(1), include));
            }

            return res;
        }

        /// <summary>
        /// Parses a log filter string, and returns the filter instance
        /// </summary>
        /// <returns>The log filter.</returns>
        /// <param name="value">The filter string to parse.</param>
        public static IFilter ParseLogFilter(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new FilterExpression();

            return value
                .Split(new char[] { System.IO.Path.PathSeparator, ':', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(StringToIFilter)
                .Aggregate(FilterExpression.Combine);
        }

        /// <summary>
        /// Helper method to support filters with either a + or - prefix
        /// </summary>
        /// <returns>The IFilter instance.</returns>
        /// <param name="msg">The filter string to parse.</param>
        public static IFilter StringToIFilter(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                return new FilterExpression();
            if (msg[0] == '+')
                return new FilterExpression(msg.Substring(1), true);
            if (msg[0] == '-')
                return new FilterExpression(msg.Substring(1), false);
            return new FilterExpression(msg, true);
        }
    }
}

