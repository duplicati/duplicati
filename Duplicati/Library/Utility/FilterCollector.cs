// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.Utility
{
    public class FilterCollector
    {
        private readonly List<Library.Utility.IFilter> m_filters = new List<Library.Utility.IFilter>();
        private Library.Utility.IFilter Filter
        {
            get
            {
                if (m_filters.Count == 0)
                    return new Library.Utility.FilterExpression();
                else if (m_filters.Count == 1)
                    return m_filters[0];
                else
                    return m_filters.Aggregate(Library.Utility.JoinedFilterExpression.Join);
            }
        }

        private Dictionary<string, string> DoExtractOptions(List<string> args, Func<string, string, bool> callbackHandler = null)
        {
            return Library.Utility.CommandLineParser.ExtractOptions(args, (key, value) =>
            {
                if (!string.IsNullOrEmpty(value))
                {
                    bool include = key.Equals("include", StringComparison.OrdinalIgnoreCase);
                    bool exclude = key.Equals("exclude", StringComparison.OrdinalIgnoreCase);

                    if (include || exclude)
                    {
                        m_filters.Add(new Library.Utility.FilterExpression(Environment.ExpandEnvironmentVariables(value), include));
                        return false;
                    }
                }

                if (callbackHandler != null)
                    return callbackHandler(key, value);

                return true;
            });
        }

        public static Tuple<Dictionary<string, string>, Library.Utility.IFilter> ExtractOptions(List<string> args, Func<string, string, bool> callbackHandler = null)
        {
            var fc = new FilterCollector();
            var opts = fc.DoExtractOptions(args, callbackHandler);
            return new Tuple<Dictionary<string, string>, Library.Utility.IFilter>(opts, fc.Filter);
        }
    }
}
