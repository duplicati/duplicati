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

