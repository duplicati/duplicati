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

using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots
{
    public interface IChangeJournalService
    {
        /// <summary>
        /// Filters sources, returning sub-set having been modified since last
        /// change, as specified by <c>journalData</c>.
        /// </summary>
        /// <param name="sources">Sources to filter</param>
        /// <param name="filterHash">A hash value representing current exclusion filter. A full scan is triggered if hash has changed.</param>
        /// <param name="journalData">The journal data of the previous backup (fileset)</param>
        /// <returns>Filtered sources</returns>
        FilterData FilterSources(IEnumerable<string> sources, string filterHash, IEnumerable<USNJournalDataEntry> journalData);
    }

    /// <summary>
    /// Filtered sources
    /// </summary>
    public struct FilterData
    {
        /// <summary>
        /// Set of potentially modified files
        /// </summary>
        public HashSet<string> Files;

        /// <summary>
        /// Set of folders that are potentially modified, or whose children
        /// are potentially modified
        /// </summary>
        public List<string> Folders;

        /// <summary>
        /// Journal data to use for next backup
        /// </summary>
        public List<USNJournalDataEntry> JournalData;
    }
}
