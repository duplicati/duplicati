// Copyright (C) 2024, The Duplicati Team
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
using System.Buffers;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// The data block represents a single blob of data read from a file
    /// </summary>
    internal sealed record DataBlock : IDisposable
    {
        public DataBlock(ArrayPool<byte> arrayPool)
            => _arrayPool = arrayPool;

        public required string HashKey { get; init; }
        public required byte[] Data { get; init; }
        public required int Offset { get; init; }
        public required long Size { get; init; }
        public required CompressionHint Hint { get; init; }
        public required bool IsBlocklistHashes { get; init; }
        public TaskCompletionSource<bool> TaskCompletion { get; } = new TaskCompletionSource<bool>();
        private readonly ArrayPool<byte> _arrayPool;
        private bool _disposed;

        public static async Task AddBlockToOutputAsync(IWriteChannel<DataBlock> channel, string hash, ArrayPool<byte> arrayPool, byte[] data, int offset, long size, CompressionHint hint, bool isBlocklistHashes)
        {
            var b = new DataBlock(arrayPool)
            {
                HashKey = hash,
                Data = data,
                Offset = offset,
                Size = size,
                Hint = hint,
                IsBlocklistHashes = isBlocklistHashes,
            };
            await channel.WriteAsync(b);

            await b.TaskCompletion.Task.ConfigureAwait(false);
        }

        public void CompleteSuccess()
        {
            Dispose();
            TaskCompletion.SetResult(true);
        }

        public void CompleteFailure(Exception ex)
        {
            Dispose();
            TaskCompletion.TrySetException(ex);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _arrayPool.Return(Data);
                _disposed = true;
            }
        }
    }
}

