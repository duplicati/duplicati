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
using Duplicati.Library.Interface;
using System.Threading.Tasks;
using CoCoL;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// The data block represents a single blob of data read from a file
    /// </summary>
    internal struct DataBlock
    {
        public string HashKey;
        public byte[] Data;
        public int Offset;
        public long Size;
        public CompressionHint Hint;
        public bool IsBlocklistHashes;
        public TaskCompletionSource<bool> TaskCompletion;

        public static async Task<bool> AddBlockToOutputAsync(IWriteChannel<DataBlock> channel, string hash, byte[] data, int offset, long size, CompressionHint hint, bool isBlocklistHashes)
        {
            var tcs = new TaskCompletionSource<bool>();

            await channel.WriteAsync(new DataBlock() {
                HashKey = hash,
                Data = data,
                Offset = offset,
                Size = size,
                Hint = hint,
                IsBlocklistHashes = isBlocklistHashes,
                TaskCompletion = tcs
            });

            var r = await tcs.Task.ConfigureAwait(false);
            return r;
        }
    }
}

