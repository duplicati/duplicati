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
using Duplicati.Library.Utility;
using static Duplicati.Library.Main.BackendManager;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal static class Channels
    {
        // TODO Should maybe come from Options, or at least some global configuration file?
        public static readonly int bufferSize = 8;

        public static readonly ChannelMarkerWrapper<Database.LocalRestoreDatabase.IFileToRestore> filesToRestore = new(new ChannelNameAttribute("filesToRestore", bufferSize));
        public static readonly ChannelMarkerWrapper<BlockRequest> downloadRequest = new(new ChannelNameAttribute("downloadRequest", bufferSize));
        public static readonly ChannelMarkerWrapper<(BlockRequest, IDownloadWaitHandle)> downloadedVolume = new(new ChannelNameAttribute("downloadResponse", bufferSize));
        public static readonly ChannelMarkerWrapper<(BlockRequest, TempFile)> decryptedVolume = new(new ChannelNameAttribute("decrytedVolume", bufferSize));
        public static readonly ChannelMarkerWrapper<(BlockRequest, byte[])> decompressedVolumes = new(new ChannelNameAttribute("decompressedVolumes", bufferSize));
    }
}