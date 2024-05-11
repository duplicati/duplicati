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
using CoCoL;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// Static typed definitions of channels used for the backup process
    /// </summary>
    internal static class Channels
    {
        /// <summary>
        /// Requests to the backend are send over this channel and picked up by the <see cref="BackendUploader" /> class
        /// </summary>
        public static readonly ChannelMarkerWrapper<IUploadRequest> BackendRequest = new ChannelMarkerWrapper<IUploadRequest>(new ChannelNameAttribute("BackendRequests"));
        /// <summary>
        /// When the backup completes, all in-progress archives are sent from the <see cref="DataBlockProcessor"/> to the <see cref="SpillCollectorProcess"/>
        /// </summary>
        public static readonly ChannelMarkerWrapper<SpillVolumeRequest> SpillPickup = new ChannelMarkerWrapper<SpillVolumeRequest>(new ChannelNameAttribute("SpillPickup"));
        /// <summary>
        /// All data blocks are sent during the scanning to the <see cref="DataBlockProcessor"/> who bundles them in compressed archives
        /// </summary>
        public static readonly ChannelMarkerWrapper<DataBlock> OutputBlocks = new ChannelMarkerWrapper<DataBlock>(new ChannelNameAttribute("OutputBlocks"));
        /// <summary>
        /// If a file has changes in the metadata, it is sent to the <see cref="FileBlockProcessor"/> where it is read
        /// </summary>
        public static readonly ChannelMarkerWrapper<MetadataPreProcess.FileEntry> AcceptedChangedFile = new ChannelMarkerWrapper<MetadataPreProcess.FileEntry>(new ChannelNameAttribute("AcceptedChangedFile"));
        /// <summary>
        /// If a file has changes in the metadata, it is sent to the <see cref="FileBlockProcessor"/> where it is read
        /// </summary>
        public static readonly ChannelMarkerWrapper<StreamBlock> StreamBlock = new ChannelMarkerWrapper<StreamBlock>(new ChannelNameAttribute("StreamBlockSplitter"));
        /// <summary>
        /// After metadata has been processed and collected, the <see cref="MetadataPreProcess"/> sends the file data to the <see cref="FilePreFilterProcess"/>
        /// </summary>
        public static readonly ChannelMarkerWrapper<MetadataPreProcess.FileEntry> ProcessedFiles = new ChannelMarkerWrapper<MetadataPreProcess.FileEntry>(new ChannelNameAttribute("ProcessedFiles"));
        /// <summary>
        /// When enumerating the source folders, all discovered paths are sent by the <see cref="FileEnumerationProcess"/> to the <see cref="MetadataPreProcess"/> 
        /// </summary>
        public static readonly ChannelMarkerWrapper<string> SourcePaths = new ChannelMarkerWrapper<string>(new ChannelNameAttribute("SourcePaths"));
        /// <summary>
        /// All progress events are communicated over this channel, to ensure that parallel progress is reported as a if it was sequential
        /// </summary>
        public static readonly ChannelMarkerWrapper<Common.ProgressEvent> ProgressEvents = new ChannelMarkerWrapper<Common.ProgressEvent>(new ChannelNameAttribute("ProgressEvents"));
    }
}

