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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// The interface for an instance of a file, as seen by a backend
    /// </summary>
    public interface IFileEntry
    {
        /// <summary>
        /// True if the entry represents a folder, false otherwise
        /// </summary>
        bool IsFolder { get; }
        /// <summary>
        /// The time the file or folder was last accessed
        /// </summary>
        DateTime LastAccess { get; }
        /// <summary>
        /// The time the file or folder was last modified
        /// </summary>
        DateTime LastModification { get; }
        /// <summary>
        /// The name of the file or folder
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The size of the file or folder
        /// </summary>
        long Size { get; }
    }
}
