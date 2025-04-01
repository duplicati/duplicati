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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// Static typed definitions of channels used for the backup process
    /// </summary>
    internal class Channels
    {
        /// <summary>
        /// When the backup completes, all in-progress archives are sent from the <see cref="DataBlockProcessor"/> to the <see cref="SpillCollectorProcess"/>
        /// </summary>
        public readonly IChannel<SpillVolumeRequest> SpillPickup = ChannelManager.CreateChannel<SpillVolumeRequest>();
        /// <summary>
        /// All data blocks are sent during the scanning to the <see cref="DataBlockProcessor"/> who bundles them in compressed archives
        /// </summary>
        public readonly IChannel<DataBlock> OutputBlocks = ChannelManager.CreateChannel<DataBlock>();
        /// <summary>
        /// If a file has changes in the metadata, it is sent to the <see cref="FileBlockProcessor"/> where it is read
        /// </summary>
        public readonly IChannel<MetadataPreProcess.FileEntry> AcceptedChangedFile = ChannelManager.CreateChannel<MetadataPreProcess.FileEntry>();
        /// <summary>
        /// If a file has changes in the metadata, it is sent to the <see cref="FileBlockProcessor"/> where it is read
        /// </summary>
        public readonly IChannel<StreamBlock> StreamBlock = ChannelManager.CreateChannel<StreamBlock>();
        /// <summary>
        /// After metadata has been processed and collected, the <see cref="MetadataPreProcess"/> sends the file data to the <see cref="FilePreFilterProcess"/>
        /// </summary>
        public readonly IChannel<MetadataPreProcess.FileEntry> ProcessedFiles = ChannelManager.CreateChannel<MetadataPreProcess.FileEntry>();
        /// <summary>
        /// When enumerating the source folders, all discovered paths are sent by the <see cref="FileEnumerationProcess"/> to the <see cref="MetadataPreProcess"/>
        /// </summary>
        public readonly IChannel<ISourceProviderEntry> SourcePaths = ChannelManager.CreateChannel<ISourceProviderEntry>();
        /// <summary>
        /// All progress events are communicated over this channel, to ensure that parallel progress is reported as a if it was sequential
        /// </summary>
        public readonly IChannel<Common.ProgressEvent> ProgressEvents = ChannelManager.CreateChannel<Common.ProgressEvent>();
    }
}

