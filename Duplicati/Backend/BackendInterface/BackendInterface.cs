#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.Backend
{
    public interface IBackendInterface
    {
        /// <summary>
        /// The name to display for this backend
        /// </summary>
        string DisplayName { get;}

        /// <summary>
        /// The protocol key, eg. ftp, http or ssh
        /// </summary>
        string ProtocolKey { get; }

        /// <summary>
        /// Returns a list of files found on the remote location
        /// </summary>
        /// <param name="url">The url passed</param>
        /// <param name="options">A list of options passed</param>
        /// <returns>The list of files</returns>
        List<FileEntry> List(string url, Dictionary<string, string> options);

        /// <summary>
        /// Puts the content of the stream to the url passed
        /// </summary>
        /// <param name="url">The url passed</param>
        /// <param name="options">A list of options passed</param>
        /// <param name="stream">The stream containing the data to use</param>
        void Put(string url, Dictionary<string, string> options, System.IO.Stream stream);

        /// <summary>
        /// Returns a stream with the remote data
        /// </summary>
        /// <param name="url">The url passed</param>
        /// <param name="options">A list of options passed</param>
        /// <returns>A stream containing the remote data</returns>
        System.IO.Stream Get(string url, Dictionary<string, string> options);

        /// <summary>
        /// Deletes the specified file
        /// </summary>
        /// <param name="url">The url passed</param>
        /// <param name="options">A list of options passed</param>
        void Delete(string url, Dictionary<string, string> options);

    }
}
