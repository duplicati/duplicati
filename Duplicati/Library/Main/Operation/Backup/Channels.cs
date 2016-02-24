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
    internal static class Channels
    {
        public static readonly ChannelMarkerWrapper<IUploadRequest> BackendRequest = new ChannelMarkerWrapper<IUploadRequest>("BackendRequests");
        public static readonly ChannelMarkerWrapper<VolumeUploadRequest> SpillPickup = new ChannelMarkerWrapper<VolumeUploadRequest>("SpillPickup");
        public static readonly ChannelMarkerWrapper<DataBlock> OutputBlocks = new ChannelMarkerWrapper<DataBlock>("OutputBlocks");
        public static readonly ChannelMarkerWrapper<MetadataPreProcess.FileEntry> AcceptedChangedFile = new ChannelMarkerWrapper<MetadataPreProcess.FileEntry>("AcceptedChangedFile");
        public static readonly ChannelMarkerWrapper<MetadataPreProcess.FileEntry> ProcessedFiles = new ChannelMarkerWrapper<MetadataPreProcess.FileEntry>("ProcessedFiles");
        public static readonly ChannelMarkerWrapper<string> SourcePaths = new ChannelMarkerWrapper<string>("SourcePaths");
        public static readonly ChannelMarkerWrapper<Common.ProgressEvent> ProgressEvents = new ChannelMarkerWrapper<Common.ProgressEvent>("ProgressEvents");
    }
}

