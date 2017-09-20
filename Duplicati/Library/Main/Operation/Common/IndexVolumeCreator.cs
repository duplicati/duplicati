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
using System.Threading.Tasks;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation.Common
{
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

