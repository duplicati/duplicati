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
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using System.Collections.Generic;
using Duplicati.Library.Main.Operation.Common;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal static class FilePreFilterProcess
    {
        public static async Task Start(Snapshots.ISnapshotService snapshot, Options options)
        {
            AutomationExtensions.RunTask(
                new
                {
                    LogChannel = ChannelMarker.ForWrite<LogMessage>("LogChannel"),
                    Input = ChannelMarker.ForRead<MetadataPreProcess.FileEntry>("ProcessedFiles"),
                    Output = ChannelMarker.ForWrite<MetadataPreProcess.FileEntry>("AcceptedChangedFile")
                },

                async self =>
                {

                    var EMPTY_METADATA = Utility.WrapMetadata(new Dictionary<string, string>(), options);
                    var blocksize = options.Blocksize;

                    while (true)
                    {
                        var e = await self.Input.ReadAsync();

                        long filestatsize = -1;
                        try
                        {
                            filestatsize = snapshot.GetFileSize(e.Path);
                        }
                        catch
                        {
                        }

                        IMetahash metahashandsize = options.StoreMetadata ? await Utility.WrapMetadata(MetadataGenerator.GenerateMetadataAsync(e.Path, e.Attributes, options, snapshot, self.LogChannel), options) : EMPTY_METADATA;

                        var timestampChanged = e.LastWrite != e.OldModified || e.LastWrite.Ticks == 0 || e.OldModified.Ticks == 0;
                        var filesizeChanged = filestatsize < 0 || e.LastFileSize < 0 || filestatsize != e.LastFileSize;
                        var tooLargeFile = options.SkipFilesLargerThan != long.MaxValue && options.SkipFilesLargerThan != 0 && filestatsize >= 0 && filestatsize > options.SkipFilesLargerThan;
                        var metadatachanged = !options.SkipMetadata && (metahashandsize.Size != e.OldMetaSize || metahashandsize.Hash != e.OldMetaHash);

                        if ((e.OldId < 0 || options.DisableFiletimeCheck || timestampChanged || filesizeChanged || metadatachanged) && !tooLargeFile)
                        {
                            await self.LogChannel.WriteAsync(LogMessage.Verbose("Checking file for changes {0}, new: {1}, timestamp changed: {2}, size changed: {3}, metadatachanged: {4}, {5} vs {6}", e.Path, e.OldId <= 0, timestampChanged, filesizeChanged, metadatachanged, e.LastWrite, e.OldModified));
                            await self.Output.WriteAsync(e);
                        }
                        else
                        {
                            if (options.SkipFilesLargerThan == long.MaxValue || options.SkipFilesLargerThan == 0 || snapshot.GetFileSize(e.Path) < options.SkipFilesLargerThan)
                                await self.LogChannel.WriteAsync(LogMessage.Verbose("Skipped checking file, because timestamp was not updated {0}", e.Path));
                            else
                                await self.LogChannel.WriteAsync(LogMessage.Verbose("Skipped checking file, because the size exceeds limit {0}", e.Path));
                        }
                    }
                }
            );
        }
    }
}

