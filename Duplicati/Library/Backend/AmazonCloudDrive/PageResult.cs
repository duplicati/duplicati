#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend.AmazonCloudDrive
{
    internal class PageResult : IPageResults
    {
        private string nextToken;
        List<FileEntry> files;

        internal PageResult(List<FileEntry> files, string nextToken)
        {
            this.files = files;
            this.nextToken = nextToken;
        }

        internal string NextToken
        {
            get
            {
                return nextToken;
            }
        }

        public IEnumerable<IFileEntry> Items
        {
            get
            {
                return files;
            }
        }

        public bool More
        {
            get
            {
                return nextToken != null;
            }
        }

    }
}
