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
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// A collection class for keeping the temporary data required to build an index file
    /// </summary>
    internal class TemporaryIndexVolume
    {
        /// <summary>
        /// The block hashes
        /// </summary>
        private readonly Library.Utility.FileBackedStringList blockHashes = new Library.Utility.FileBackedStringList();

        /// <summary>
        /// The blocklist hashes
        /// </summary>
        private readonly Library.Utility.FileBackedStringList blockListHashes = new Library.Utility.FileBackedStringList();

        /// <summary>
        /// Cached copy of the blocklist hash size
        /// </summary>
		private readonly int m_blockhashsize;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="T:Duplicati.Library.Main.Operation.Common.TemporaryIndexVolume"/> class.
        /// </summary>
        /// <param name="options">The options used in this run.</param>
		public TemporaryIndexVolume(Options options)
		{
			m_blockhashsize = options.BlockhashSize;
		}

        /// <summary>
        /// Creates an index volume with the temporary contents
        /// </summary>
        /// <returns>The index volume.</returns>
        /// <param name="blockfilename">The name of the block file.</param>
        public async Task<IndexVolumeWriter> CreateVolume(string blockfilename, Options options, Common.DatabaseCommon database)
        {
            var w = new IndexVolumeWriter(options);
            w.VolumeID = await database.RegisterRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);

            var blockvolume = await database.GetVolumeInfoAsync(blockfilename);

            w.StartVolume(blockfilename);
            foreach (var n in blockHashes)
            {
                var args = n.Split(new char[] { ':' }, 2);
                w.AddBlock(args[1], long.Parse(args[0]));
            }
            
            w.FinishVolume(blockvolume.Hash, blockvolume.Size);

            var enumerator = blockListHashes.GetEnumerator();
            while(enumerator.MoveNext())
            {
                var hash = enumerator.Current;
                enumerator.MoveNext();
                var data = Convert.FromBase64String(enumerator.Current);

                w.WriteBlocklist(hash, data, 0, data.Length);
            }

            w.Close();

            return w;
        }

        /// <summary>
        /// Copies all entries from this temporary instance to the target
        /// </summary>
        /// <param name="target">The target volume.</param>
        /// <param name="onlyBlocklistHashes">Only copies the blocklist hashes</param>
        public void CopyTo(TemporaryIndexVolume target, bool onlyBlocklistHashes)
        {
            if (!onlyBlocklistHashes)
                foreach (var n in blockHashes)
                    target.blockHashes.Add(n);
            
            foreach (var n in blockListHashes)
                target.blockListHashes.Add(n);
        }

        /// <summary>
        /// Adds a single block hash to the index
        /// </summary>
        /// <param name="hash">The hash of the block.</param>
        /// <param name="size">The size of the block.</param>
        public void AddBlock(string hash, long size)
        {
            blockHashes.Add(size.ToString() + ":" + hash);
        }

        /// <summary>
        /// Adds a block list hash to the index
        /// </summary>
        /// <param name="hash">The hash of the block to add.</param>
        /// <param name="size">The size of the block.</param>
        /// <param name="data">The block contents.</param>
        public void AddBlockListHash(string hash, long size, byte[] data)
        {
			if (size % m_blockhashsize != 0)
				throw new ArgumentException($"The {nameof(size)} value is {size}, but it must be evenly divisible by the blockhash size ({m_blockhashsize})", nameof(size));
            blockListHashes.Add(hash);
            blockListHashes.Add(Convert.ToBase64String(data, 0, (int)size));
        }
    }

    /// <summary>
    /// Encapsulates creating an index file from the database contents
    /// </summary>
    internal static class IndexVolumeCreator
    {
        public static async Task<IndexVolumeWriter> CreateIndexVolume(string blockname, Options options, Common.DatabaseCommon database)
        {
            using(var h = Duplicati.Library.Utility.HashAlgorithmHelper.Create(options.BlockHashAlgorithm))
            {
                var w = new IndexVolumeWriter(options);
                w.VolumeID = await database.RegisterRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);

                var blockvolume = await database.GetVolumeInfoAsync(blockname);

                w.StartVolume(blockname);
                foreach(var b in await database.GetBlocksAsync(blockvolume.ID))
                    w.AddBlock(b.Hash, b.Size);

                w.FinishVolume(blockvolume.Hash, blockvolume.Size);

                if (options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                    foreach(var b in await database.GetBlocklistsAsync(blockvolume.ID, options.Blocksize, options.BlockhashSize))
                    {
                        var bh = Convert.ToBase64String(h.ComputeHash(b.Item2, 0, b.Item3));
                        if (bh != b.Item1)
                            throw new Exception(string.Format("Internal consistency check failed, generated index block has wrong hash, {0} vs {1}", bh, b.Item1));
                        w.WriteBlocklist(b.Item1, b.Item2, 0, b.Item3);
                    }

                w.Close();

                // Register that the index file is tracking the block file
                await database.AddIndexBlockLinkAsync(w.VolumeID, blockvolume.ID);

                return w;
            }
        }

        /*public static async Task<IndexVolumeWriter> ReCreateIndexVolume(string selfname, Options options, Repair.RepairDatabase database)
        {
            using(var h = System.Security.Cryptography.HashAlgorithm.Create(options.BlockHashAlgorithm))
            {
                var w = new IndexVolumeWriter(options);
                w.SetRemoteFilename(selfname);

                foreach(var blockvolume in await database.GetBlockVolumesFromIndexNameAsync(selfname))
                {                               
                    w.StartVolume(blockvolume.Name);
                    var volumeid = await database.GetRemoteVolumeIDAsync(blockvolume.Name);

                    foreach(var b in await database.GetBlocksAsync(volumeid))
                        w.AddBlock(b.Hash, b.Size);

                    w.FinishVolume(blockvolume.Hash, blockvolume.Size);

                    if (options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                        foreach(var b in await database.GetBlocklistsAsync(volumeid, options.Blocksize, options.BlockhashSize))
                        {
                            var bh = Convert.ToBase64String(h.ComputeHash(b.Item2, 0, b.Item3));
                            if (bh != b.Item1)
                                throw new Exception(string.Format("Internal consistency check failed, generated index block has wrong hash, {0} vs {1}", bh, b.Item1));
                            w.WriteBlocklist(b.Item1, b.Item2, 0, b.Item3);
                        }
                }

                w.Close();

                return w;
            }
        }*/
    }
}

