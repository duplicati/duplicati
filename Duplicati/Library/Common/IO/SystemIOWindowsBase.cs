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
using System.Security.AccessControl;
using Newtonsoft.Json;

namespace Duplicati.Library.Common.IO
{
    public abstract class SystemIOWindowsBase
    {
        protected const string MetadataOwnerKey = "win-ext:owner";
        protected const string MetadataAccessRulesKey = "win-ext:accessrules";
        protected const string MetadataAccessRulesIsProtectedKey = "win-ext:accessrulesprotected";

        protected const string UNCPREFIX = @"\\?\";
        protected const string UNCPREFIX_SERVER = @"\\?\UNC\";
        protected const string PATHPREFIX_SERVER = @"\\";
        protected static readonly string DIRSEP = Util.DirectorySeparatorString;


        public static string PrefixWithUNC(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return path;
            }
            return path.StartsWith(PATHPREFIX_SERVER, StringComparison.Ordinal)
                ? UNCPREFIX_SERVER + path.Substring(PATHPREFIX_SERVER.Length)
                : UNCPREFIX + path;
        }

        internal static bool IsPrefixedWithUNC(string path)
        {
            return path.StartsWith(UNCPREFIX_SERVER, StringComparison.Ordinal) ||
                path.StartsWith(UNCPREFIX, StringComparison.Ordinal);
        }

        public static string StripUNCPrefix(string path)
        {
            if (path.StartsWith(UNCPREFIX_SERVER, StringComparison.Ordinal))
            {
                // @"\\?\UNC\example.com\share\file.txt" to @"\\example.com\share\file.txt"
                return PATHPREFIX_SERVER + path.Substring(UNCPREFIX_SERVER.Length);
            }
            else if (path.StartsWith(UNCPREFIX, StringComparison.Ordinal))
            {
                // @"\\?\C:\file.txt" to @"C:\file.txt"
                return path.Substring(UNCPREFIX.Length);
            }
            else
            {
                return path;
            }
        }

        /// <summary>
        /// Convert forward slashes to backslashes.
        /// </summary>
        /// <returns>Path with forward slashes replaced by backslashes.</returns>
        internal static string ConvertSlashes(string path)
        {
            return path.Replace("/", Util.DirectorySeparatorString);
        }



        private static Newtonsoft.Json.JsonSerializer _cachedSerializer;

        protected Newtonsoft.Json.JsonSerializer Serializer
        {
            get
            {
                if (_cachedSerializer != null)
                {
                    return _cachedSerializer;
                }

                _cachedSerializer = Newtonsoft.Json.JsonSerializer.Create(
                    new Newtonsoft.Json.JsonSerializerSettings { Culture = System.Globalization.CultureInfo.InvariantCulture });

                return _cachedSerializer;
            }
        }

        protected string SerializeObject<T>(T o)
        {
            using (var tw = new System.IO.StringWriter())
            {
                Serializer.Serialize(tw, o);
                tw.Flush();
                return tw.ToString();
            }
        }

        protected T DeserializeObject<T>(string data)
        {
            using (var tr = new System.IO.StringReader(data))
            {
                return (T)Serializer.Deserialize(tr, typeof(T));
            }
        }
    }
}

