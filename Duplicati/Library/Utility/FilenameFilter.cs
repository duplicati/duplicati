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
    /// This class handles exclusion and inclusion of filenames, based on supplied patterns
    /// </summary>
    public class FilenameFilter
    {
        private List<IFilenameFilter> m_filters;

        //We cannot use the regular command line parser, because we must preserve the order of the expressions
        /// <summary>
        /// All commandline parameters are position ignorant, except filters.
        /// To preserve the order, we must parse these before parsing the regular options.
        /// </summary>
        /// <param name="commandline">The commandline arguments</param>
        /// <param name="remove">True if filter arguments should be removed from the list</param>
        /// <returns>A parsed ordered set of filters</returns>
        public static List<KeyValuePair<bool, string>> ParseCommandLine(List<string> commandline, bool remove)
        {
            //TODO: Support the options --include-filelist, --include-globbin-filelist, --include-regexp-filelist
            //and the exclude variants (all work on the lines in a file)

            List<KeyValuePair<bool, string>> lst = new List<KeyValuePair<bool, string>>();
            Regex includeOrExclude = new Regex(@"(?<prefix>(\-\-include)|(\-\-exclude)|(\-\-include\-regexp)|(\-\-exclude\-regexp))(?<eq>\=)(?<content>.+)", RegexOptions.IgnoreCase);
            for (int i = 0; i < commandline.Count; i++ )
            {
                string s = commandline[i];
                Match m = includeOrExclude.Match(s.ToLower());
                if (m.Success)
                {
                    bool include = m.Groups["prefix"].Value.ToLower() == "--include" || m.Groups["prefix"].Value.ToLower() == "--include-regexp";
                    string cmd = m.Groups["content"].Value;
                    if (!m.Groups["prefix"].Value.ToLower().EndsWith("-regexp"))
                        cmd = ConvertGlobbingToRegExp(cmd);

                    //It is annoying to use backslashes on windows, because they are also escape
                    //controls, so \t has to be \\t and on the command line it must be \\\\t, so we
                    //accept the unix style dir separator "/"
                    cmd = cmd.Replace("/", ConvertGlobbingToRegExp(Utility.DirectorySeparatorString));

                    lst.Add(new KeyValuePair<bool, string>(include, cmd));
                    if (remove)
                    {
                        commandline.RemoveAt(i);
                        i--;
                    }
                }
            }
            return lst;
        }

        /// <summary>
        /// To pass the order preserving filter around, we must encode multiple command
        /// line arguments as a single option.
        /// </summary>
        /// <param name="items">The filter items to encode into a single option</param>
        /// <returns>An encoded string with the arguments</returns>
        public static string EncodeAsFilter(List<KeyValuePair<bool, string>> items)
        {
            StringBuilder sb = new StringBuilder();
            foreach(KeyValuePair<bool, string> x in items)
            {
                if (sb.Length != 0)
                    sb.Append(System.IO.Path.PathSeparator.ToString());
                sb.Append(x.Key ? "i:" : "e:");
                if (x.Value.Contains(System.IO.Path.PathSeparator.ToString()))
                    throw new Exception(string.Format(Strings.FilenameFilter.FilterContainsPathSeparator, System.IO.Path.PathSeparator));
                sb.Append(x.Value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// When all filters are encoded into a single filter option, it must be decoded.
        /// </summary>
        /// <param name="options">The encoded filter string</param>
        /// <returns>The decoded ordered list of filters</returns>
        public static List<KeyValuePair<bool, string>> DecodeFilter(string filter)
        {
            List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();

            if (string.IsNullOrEmpty(filter))
                return filters;

            int x = 0;
            do
            {
                x = filter.IndexOf(System.IO.Path.PathSeparator, x);
                
                //HACK: Detection of linux special case
                //TODO: Reconsider using the ":" character, as that is the PathSeparator on linux
                if (System.IO.Path.PathSeparator == ':' && x == 1)
                    x = filter.IndexOf(System.IO.Path.PathSeparator, x + 1);

                if (x > 0)
                {
                    string fx = filter.Substring(0, x);
                    filters.Add(new KeyValuePair<bool, string>(fx.StartsWith("i:"), fx.Substring(2)));
                    filter = filter.Substring(x + 1);
                    x = 0;
                }
            } while (x >= 0);

            if (filter.Length > 0)
                filters.Add(new KeyValuePair<bool, string>(filter.StartsWith("i:"), filter.Substring(2)));

            return filters;
        }


        /// <summary>
        /// When all filters are encoded into a single filter option, it must be decoded.
        /// </summary>
        /// <param name="options">The list of options, which may have a filter option</param>
        /// <returns>The decoded ordered list of filters</returns>
        private static List<KeyValuePair<bool, string>> ExtractOptionFilter(Dictionary<string, string> options)
        {
            if (options.ContainsKey("filter"))
                return DecodeFilter(options["filter"]);
            else
                return new List<KeyValuePair<bool, string>>();
        }

        /// <summary>
        /// These are characters that must be escaped when using a globbing expression
        /// </summary>
        private static readonly string BADCHARS = "\\" + string.Join("|\\", new string[] {
                "\\",
                "+",
                "|",
                "{",
                "[",
                "(",
                ")",
                "]",
                "}",
                "^",
                "$",
                "#",
                "."
            });

        /// <summary>
        /// Most people will probably want to use fileglobbing, but RegExp's are more flexible.
        /// By converting from the weak globbing to the stronger regexp, we support both.
        /// </summary>
        /// <param name="globexp"></param>
        /// <returns></returns>
        public static string ConvertGlobbingToRegExp(string globexp)
        {
            //First escape all special characters
            globexp = Regex.Replace(globexp, BADCHARS, "\\$&");

            //Replace the globbing expressions with the corresponding regular expressions
            globexp = globexp.Replace('?', '.').Replace("*", ".*");
            return globexp;
        }

        public FilenameFilter(Dictionary<string, string> options)
            : this(ExtractOptionFilter(options))
        {
        }

        public FilenameFilter(List<string> commandline)
            : this(ParseCommandLine(commandline, false))
        {
        }

        public FilenameFilter(List<IFilenameFilter> filters)
        {
            m_filters = filters;
        }

        public FilenameFilter(List<KeyValuePair<bool, string>> filters)
        {
            m_filters = new List<IFilenameFilter>();
            foreach (KeyValuePair<bool, string> filter in InlineCompact(filters))
                m_filters.Add(new RegularExpressionFilter(filter.Key, filter.Value));
        }

        /// <summary>
        /// Combines multiple expressions into a single expression where possible
        /// </summary>
        /// <param name="filters">The list of expressions to compact</param>
        /// <returns>The compacted expressions</returns>
        private List<KeyValuePair<bool, string>> InlineCompact(List<KeyValuePair<bool, string>> filters)
        {
            if (filters.Count == 0)
                return filters;

            List<KeyValuePair<bool, string>> res = new List<KeyValuePair<bool, string>>();
            bool include = !filters[0].Key;
            foreach (KeyValuePair<bool, string> filter in filters)
            {
                if (filter.Key != include)
                {
                    res.Add(new KeyValuePair<bool, string>(filter.Key, "(" + filter.Value + ")"));
                    include = filter.Key;
                }
                else
                    res[res.Count - 1] = new KeyValuePair<bool, string>(filter.Key, res[res.Count - 1].Value + "|(" + filter.Value + ")");
            }

            return res;
        }
        public bool ShouldInclude(string basepath, string filename)
        {
            IFilenameFilter dummy;
            return ShouldInclude(basepath, filename, out dummy);
        }

        public bool ShouldInclude(string basepath, string filename, out IFilenameFilter match)
        {
            match = null;
            basepath = Utility.AppendDirSeparator(basepath);
            if (!filename.StartsWith(basepath, Utility.ClientFilenameStringComparision))
                return false;

            //All paths start with a slash, because this eases filter creation
            //Eg. filter "\Dir\" would only match folders with the name "Dir", where "Dir\" would also match "\MyDir\"
            //If the leading slash/backslash is missing, it becomes difficult to prevent partial matches.
            string relpath = filename.Substring(basepath.Length - 1);

            //Run through each filter, test for relpath and full path
            foreach (IFilenameFilter filter in m_filters)
                if (filter.Match(relpath) || filter.Match(filename))
                {
                    match = filter;
                    return filter.Include;
                }

            return true;
        }

        public List<string> FilterList(string basepath, IEnumerable<string> filenames)
        {
            basepath = Utility.AppendDirSeparator(basepath);

            List<string> included = new List<string>();
            foreach (string s in filenames)
                if (ShouldInclude(basepath, s))
                    included.Add(s);
            
            return included;
        }
    }
}