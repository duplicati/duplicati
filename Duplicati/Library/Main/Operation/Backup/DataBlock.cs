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

