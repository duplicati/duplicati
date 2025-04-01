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

using CoCoL;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{

    /// <summary>
    /// Named channels for the restore operation.
    /// </summary>
    internal class Channels
    {
        /// <summary>
        /// The buffer size for the channels. The buffer size is the number of
        /// messages that can be queued up before the sender blocks.
        /// </summary>
        public static int BufferSize;

        /// <summary>
        /// Channel between <see cref="FileLister"/> and <see cref="FileProcessor"/>.
        /// </summary>
        public readonly IChannel<FileRequest> FilesToRestore = ChannelManager.CreateChannel<FileRequest>(buffersize: BufferSize);

        /// <summary>
        /// Channel between <see cref="VolumeManager"/> and <see cref="VolumeDownloader"/>.
        /// </summary>
        public readonly IChannel<long> DownloadRequest = ChannelManager.CreateChannel<long>(buffersize: BufferSize);

        /// <summary>
        /// Channel between <see cref="VolumeDownloader"/> and <see cref="VolumeDecryptor"/>
        /// </summary>
        public readonly IChannel<(long, string, TempFile)> DecryptRequest = ChannelManager.CreateChannel<(long, string, TempFile)>(buffersize: BufferSize);

        /// <summary>
        /// Channel between <see cref="VolumeManager"/> and <see cref="VolumeDecompressor"/>
        /// </summary>
        public readonly IChannel<(BlockRequest, BlockVolumeReader)> DecompressionRequest = ChannelManager.CreateChannel<(BlockRequest, BlockVolumeReader)>(buffersize: BufferSize);

        /// <summary>
        /// Channel between <see cref="VolumeManager"/> and <see cref="BlockManager"/> holding the decompressed blocks.
        public readonly IChannel<(BlockRequest, byte[])> DecompressedBlock = ChannelManager.CreateChannel<(BlockRequest, byte[])>(buffersize: BufferSize);

        /// <summary>
        /// Channel between <see cref="VolumeManager"/> and <see cref="BlockManager"/> / <see cref="VolumeDecryptor"/>, used for requesting and responding volumes to the manager.
        /// </summary>
        public readonly IChannel<object> VolumeRequestResponse = ChannelManager.CreateChannel<object>(buffersize: BufferSize);
    }

}