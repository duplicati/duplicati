//  Copyright (C) 2018, The Duplicati Team
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

using System.Text;

namespace Duplicati.Library.Utility
{
    public class UrlPath
    {
        /// <summary>
        /// Concats paths of URIs.
        /// </summary>
        /// <returns>The concatenated paths.</returns>
        /// <param name="path">Path1.</param>

        StringBuilder paths;
        bool trailingSlash = false;

        public UrlPath(string path)
        {
            paths = new StringBuilder(path.TrimEnd('/'));
        }

        public static UrlPath Create(string path)
        {
            return new UrlPath(path);
        }


        public UrlPath Append(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return this;
            }

            paths.Append("/").Append(path.Trim('/'));
            trailingSlash = '/'.Equals(path[path.Length-1]);
            return this;
        }

        public override string ToString() 
        {
            return paths.ToString() + (trailingSlash ? "/" : string.Empty);
        }
    }
}
