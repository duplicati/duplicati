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

using CoCoL;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using static Duplicati.Library.Main.BackendManager;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Named channels for the restore operation.
    /// </summary>
    internal static class Channels
    {
        /// <summary>
        /// The buffer size for the channels. The buffer size is the number of
        /// messages that can be queued up before the sender blocks.
        /// </summary>
        public static readonly int BufferSize = 1024;

        /// <summary>
        /// Channel between <see cref="FileLister"/> and <see cref="FileProcessor"/>.
        /// </summary>
        public static readonly ChannelMarkerWrapper<Database.LocalRestoreDatabase.IFileToRestore> FilesToRestore = new(new ChannelNameAttribute("FilesToRestore", BufferSize));

        /// <summary>
        /// Channel between <see cref="BlockManager"/> and <see cref="VolumeCache"/>.
        /// </summary>
        public static readonly ChannelMarkerWrapper<BlockRequest> BlockFetch = new(new ChannelNameAttribute("BlockFetch", BufferSize));

        /// <summary>
        /// Channel between <see cref="VolumeCache"/> and <see cref="VolumeDownloader"/>.
        /// </summary>
        public static readonly ChannelMarkerWrapper<(long, IDownloadWaitHandle)> DownloadRequest = new(new ChannelNameAttribute("DownloadRequest", BufferSize));

        /// <summary>
        /// Channel between <see cref="VolumeDownloader"/> and <see cref="VolumeDecryptor"/>
        /// </summary>
        public static readonly ChannelMarkerWrapper<(long, TempFile)> DecryptRequest = new(new ChannelNameAttribute("DecryptRequest", BufferSize));

        /// <summary>
        /// Channel between <see cref="VolumeDecryptor"/> and <see cref="VolumeCache"/>
        /// </summary>
        public static readonly ChannelMarkerWrapper<(long, TempFile, BlockVolumeReader)> DecryptedVolume = new(new ChannelNameAttribute("DecryptedVolume", BufferSize));

        /// <summary>
        /// Channel between <see cref="VolumeCache"/> and <see cref="VolumeDecompressor"/>
        /// </summary>
        public static readonly ChannelMarkerWrapper<(BlockRequest, BlockVolumeReader)> DecompressionRequest = new(new ChannelNameAttribute("DecompressionRequest", BufferSize));

        /// <summary>
        /// Channel between <see cref="VolumeDecompressor"/> and <see cref="BlockManager"/>
        /// </summary>
        public static readonly ChannelMarkerWrapper<(BlockRequest, byte[])> DecompressedBlock = new(new ChannelNameAttribute("DecompressedBlock", BufferSize));
    }

}