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
        public static readonly int bufferSize = 1024;

        /// <summary>
        /// The channel between the `FileLister` and `FileProcessor` processes.
        /// </summary>
        public static readonly ChannelMarkerWrapper<Database.LocalRestoreDatabase.IFileToRestore> filesToRestore = new(new ChannelNameAttribute("filesToRestore", bufferSize));

        /// <summary>
        /// The channel between the `BlockManager` and `VolumeDownloader` processes.
        /// </summary>
        public static readonly ChannelMarkerWrapper<BlockRequest> downloadRequest = new(new ChannelNameAttribute("downloadRequest", bufferSize));

        /// <summary>
        /// The channel between the `VolumeDownloader` and `VolumeDecrypter` processes.
        /// </summary>
        public static readonly ChannelMarkerWrapper<(BlockRequest, IDownloadWaitHandle)> downloadedVolume = new(new ChannelNameAttribute("downloadResponse", bufferSize));

        /// <summary>
        /// The channel between the `VolumeDecrypter` and `VolumeDecompressor` processes.
        /// </summary>
        public static readonly ChannelMarkerWrapper<(BlockRequest, TempFile)> decryptedVolume = new(new ChannelNameAttribute("decrytedVolume", bufferSize));

        /// <summary>
        /// The channel between the `VolumeDecompressor` and `BlockManager` processes.
        /// </summary>
        public static readonly ChannelMarkerWrapper<(BlockRequest, BlockVolumeReader)> decompressedVolumes = new(new ChannelNameAttribute("decompressedVolumes", bufferSize));
    }

}