﻿//  Copyright (C) 2015, The Duplicati Team
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
