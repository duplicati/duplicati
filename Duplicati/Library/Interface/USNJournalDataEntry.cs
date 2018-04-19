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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Data representing the state of an NTFS USN (Change) Journal
    /// </summary>
    public class USNJournalDataEntry
    {
        /// <summary>
        /// Volume of journal
        /// </summary>
        public string Volume;

        /// <summary>
        /// Journal ID, as recorded in the journal
        /// </summary>
        public long JournalId;

        /// <summary>
        /// USN to start at for next fileset (backup)
        /// </summary>
        public long NextUsn;

        /// <summary>
        /// A hash value representing the active exclusion filter
        /// </summary>
        public string ConfigHash;
    }
}
