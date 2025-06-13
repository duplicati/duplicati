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

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Represents a block request that the `VolumeDownloader` process will use to download a block from the backend.
    /// </summary>
    /// <param name="blockID">The block ID in the database.</param>
    /// <param name="blockOffset">The offset in blocks this block represents in the target file.</param>
    /// <param name="blockHash">The hash of the block.</param>
    /// <param name="blockSize">The size of the block.</param>
    /// <param name="volumeID">The ID of the volume in which the block is stored remotely.</param>
    /// <param name="cacheDecrEvict">Flag indicating that this block request should either decrement the block counter for BlockID (for BlockManager) or evict the VolumeID (for VolumeDownloader).</param>
    public class BlockRequest(long blockID, long blockOffset, string blockHash, long blockSize, long volumeID, bool cacheDecrEvict)
    { // Total = 77 bytes
        public long BlockID { get; } = blockID;
        public long BlockOffset { get; } = blockOffset;
        public string BlockHash { get; } = blockHash;
        public long BlockSize { get; } = blockSize;
        public long VolumeID { get; } = volumeID;
        public bool CacheDecrEvict { get; set; } = cacheDecrEvict;
    }

    /// <summary>
    /// Represents a file request that the `FileLister` process sends to the `FileProcessor` process.
    /// </summary>
    /// <param name="ID">The File ID.</param>
    /// <param name="OriginalPath">The original path of the file.</param>
    /// <param name="TargetPath">The target path of the file.</param>
    /// <param name="Hash">The file hash.</param>
    /// <param name="Length">The length of the file.</param>
    /// <param name="BlocksetID">The BlocksetID of the file.</param>
    public class FileRequest(long ID, string OriginalPath, string TargetPath, string Hash, long Length, long BlocksetID)
    {
        public long ID { get; } = ID;
        public string OriginalPath { get; } = OriginalPath;
        public string TargetPath { get; } = TargetPath;
        public string Hash { get; } = Hash;
        public long Length { get; } = Length;
        public long BlocksetID { get; } = BlocksetID;
    }

}