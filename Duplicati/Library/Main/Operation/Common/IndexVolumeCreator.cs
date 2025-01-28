// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Common
{
    // TODO: Remove this class and simply rely on the database to create the index files

    /// <summary>
    /// A collection class for keeping the temporary data required to build an index file
    /// </summary>
    internal class TemporaryIndexVolume
    {
        /// <summary>
        /// The block hashes
        /// </summary>
        private readonly FileBackedStringList blockHashes = new();

        /// <summary>
        /// The blocklist hashes
        /// </summary>
        private readonly FileBackedStringList blockListHashes = new();

        /// <summary>
        /// The known block list hashes
        /// </summary>
        private readonly HashSet<string> knownBlockListHashes = new();

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
        /// <param name="options">The options used in this run.</param>
        /// <param name="database">The database to use.</param>
        public async Task<IndexVolumeWriter> CreateVolume(string blockfilename, Options options, DatabaseCommon database)
        {
            var w = new IndexVolumeWriter(options);
            w.VolumeID = await database.RegisterRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);

            var enumerator = blockListHashes.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var hash = enumerator.Current;
                enumerator.MoveNext();
                var data = Convert.FromBase64String(enumerator.Current);

                w.WriteBlocklist(hash, data, 0, data.Length);
            }

            w.StartVolume(blockfilename);
            foreach (var n in blockHashes)
            {
                var args = n.Split(new char[] { ':' }, 2);
                w.AddBlock(args[1], long.Parse(args[0]));
            }

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

            var enumerator = blockListHashes.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var hash = enumerator.Current;
                enumerator.MoveNext();

                // Filter duplicates
                if (target.knownBlockListHashes.Contains(hash))
                    continue;

                target.knownBlockListHashes.Add(hash);
                target.blockListHashes.Add(hash);
                target.blockListHashes.Add(enumerator.Current);
            }
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
            // Filter out duplicates to reduce size
            if (knownBlockListHashes.Contains(hash))
                return;
            knownBlockListHashes.Add(hash);

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
            using (var h = HashFactory.CreateHasher(options.BlockHashAlgorithm))
            {
                var w = new IndexVolumeWriter(options);
                w.VolumeID = await database.RegisterRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);

                var blockvolume = await database.GetVolumeInfoAsync(blockname);

                w.StartVolume(blockname);
                foreach (var b in await database.GetBlocksAsync(blockvolume.ID))
                    w.AddBlock(b.Hash, b.Size);

                w.FinishVolume(blockvolume.Hash, blockvolume.Size);

                if (options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                    foreach (var b in await database.GetBlocklistsAsync(blockvolume.ID, options.Blocksize, options.BlockhashSize))
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
    }
}

