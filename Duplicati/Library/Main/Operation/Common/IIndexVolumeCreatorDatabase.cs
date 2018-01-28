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
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// An interface for the operations needed by the <see cref="IndexVolumeCreator"/> routine
    /// </summary>
    internal interface IIndexVolumeCreatorDatabase
    {
        /// <summary>
        /// Creates and registers a remote volume
        /// </summary>
        /// <returns>The newly created volume ID.</returns>
        /// <param name="name">The name of the remote file.</param>
        /// <param name="type">The type of the remote file.</param>
        /// <param name="state">The state of the remote file.</param>
        Task<long> RegisterRemoteVolumeAsync(string name, RemoteVolumeType type, RemoteVolumeState state);

        /// <summary>
        /// Gets a list of all blocks associated with a given volume
        /// </summary>
        /// <returns>The blocks found in the volume.</returns>
        /// <param name="volumeid">The ID of the volume to examine.</param>
        Task<IEnumerable<Database.LocalDatabase.IBlock>> GetBlocksAsync(long volumeid);

        /// <summary>
        /// Gets the blocklists contained in a remote volume
        /// </summary>
        /// <returns>The blocklists.</returns>
        /// <param name="volumeid">The ID of the volume to get the blocklists for.</param>
        /// <param name="blocksize">The blocksize setting.</param>
        /// <param name="hashsize">The size of the hash in bytes.</param>
        Task<IEnumerable<Tuple<string, byte[], int>>> GetBlocklistsAsync(long volumeid, int blocksize, int hashsize);

        /// <summary>
        /// Adds a link between a block volume and an index volume
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="indexVolumeID">The index volume ID.</param>
        /// <param name="blockVolumeID">The block volume ID.</param>
        Task AddIndexBlockLinkAsync(long indexVolumeID, long blockVolumeID);        

        /// <summary>
        /// Gets the volume information for a remote file, given the name.
        /// </summary>
        /// <returns>The remote volume information.</returns>
        /// <param name="remotename">The name of the remote file to query.</param>
        Task<Database.RemoteVolumeEntry> GetVolumeInfoAsync(string remotename);
    }
}
