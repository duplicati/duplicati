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
    /// <summary>
    /// This process takes files that are processed for metadata, 
    /// and checks if anything indicates that the file has changed
    /// and submits potentially changed files for scanning
    /// </summary>
    internal static class FilePreFilterProcess
    {
        /// <summary>
        /// The tag to use for log messages
        /// </summary>
        private static readonly string FILELOGTAG = Logging.Log.LogTagFromType(typeof(FilePreFilterProcess)) + ".FileEntry";

        public static Task Run(Snapshots.ISnapshotService snapshot, Options options, BackupStatsCollector stats, BackupDatabase database)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.ProcessedFiles.ForRead,
                ProgressChannel = Channels.ProgressEvents.ForWrite,
                Output = Channels.AcceptedChangedFile.ForWrite
            },

            async self =>
            {
                var EMPTY_METADATA = Utility.WrapMetadata(new Dictionary<string, string>(), options);

                // Pre-cache the option variables here to simplify and
                // speed up repeated option access below

                var SKIPFILESLARGERTHAN = options.SkipFilesLargerThan;
                // Zero and max both indicate no size limit
                if (SKIPFILESLARGERTHAN == long.MaxValue)
                    SKIPFILESLARGERTHAN = 0;

                var DISABLEFILETIMECHECK = options.DisableFiletimeCheck;
                var CHECKFILETIMEONLY = options.CheckFiletimeOnly;
                var SKIPMETADATA = options.SkipMetadata;

                while (true)
                {
                    var e = await self.Input.ReadAsync();

                    long filestatsize = -1;
                    try
                    {
                        filestatsize = snapshot.GetFileSize(e.Path);
                    }
                    catch(Exception ex)
                    {
                        Logging.Log.WriteExplicitMessage(FILELOGTAG, "FailedToReadSize", ex, "Failed to read size of file: {0}", e.Path);
                    }

                    await stats.AddExaminedFile(filestatsize);

                    // Stop now if the file is too large
                    var tooLargeFile = SKIPFILESLARGERTHAN != 0 && filestatsize >= 0 && filestatsize > SKIPFILESLARGERTHAN;
                    if (tooLargeFile)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "SkipCheckTooLarge", "Skipped checking file, because the size exceeds limit {0}", e.Path);
                        await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = filestatsize, Type = EventType.FileSkipped });
                        continue;
                    }

                    // Invalid ID indicates a new file
                    var isNewFile = e.OldId < 0;

                    // If we disable the filetime check, we always assume that the file has changed
                    // Otherwise we check that the timestamps are different or if any of them are empty
                    var timestampChanged = DISABLEFILETIMECHECK || e.LastWrite != e.OldModified || e.LastWrite.Ticks == 0 || e.OldModified.Ticks == 0;

                    // Avoid generating a new metadata blob if timestamp has not changed
                    // and we only check for timestamp changes
                    if (CHECKFILETIMEONLY && !timestampChanged && !isNewFile)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "SkipCheckNoTimestampChange", "Skipped checking file, because timestamp was not updated {0}", e.Path);                                                
                        try
                        {
                            await database.AddUnmodifiedAsync(e.OldId, e.LastWrite);
                        }
                        catch (Exception ex)
                        {
                            if (ex.IsRetiredException())
                                throw;
                            Logging.Log.WriteWarningMessage(FILELOGTAG, "FailedToAddFile", ex, "Failed while attempting to add unmodified file to database: {0}", e.Path);
                        }
                        await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = filestatsize, Type = EventType.FileSkipped });
                        continue;
                    }

                    // If we have have disabled the filetime check, we do not have the metadata info
                    // but we want to know if the metadata is potentially changed
                    if (!isNewFile && DISABLEFILETIMECHECK)
                    {
                        var tp = await database.GetMetadataHashAndSizeForFileAsync(e.OldId);
                        if (tp != null)
                        {
                            e.OldMetaSize = tp.Item1;
                            e.OldMetaHash = tp.Item2;
                        }
                    }

                    // Compute current metadata
                    e.MetaHashAndSize = SKIPMETADATA ? EMPTY_METADATA : Utility.WrapMetadata(MetadataGenerator.GenerateMetadata(e.Path, e.Attributes, options, snapshot), options);
                    e.MetadataChanged = !SKIPMETADATA && (e.MetaHashAndSize.Blob.Length != e.OldMetaSize || e.MetaHashAndSize.FileHash != e.OldMetaHash);

                    // Check if the file is new, or something indicates a change
                    var filesizeChanged = filestatsize < 0 || e.LastFileSize < 0 || filestatsize != e.LastFileSize;
                    if (isNewFile || timestampChanged || filesizeChanged || e.MetadataChanged)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "CheckFileForChanges", "Checking file for changes {0}, new: {1}, timestamp changed: {2}, size changed: {3}, metadatachanged: {4}, {5} vs {6}", e.Path, isNewFile, timestampChanged, filesizeChanged, e.MetadataChanged, e.LastWrite, e.OldModified);
                        await self.Output.WriteAsync(e);
                    }
                    else
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "SkipCheckNoMetadataChange", "Skipped checking file, because no metadata was updated {0}", e.Path);
                        try
                        {
                            await database.AddUnmodifiedAsync(e.OldId, e.LastWrite);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(FILELOGTAG, "FailedToAddFile", ex, "Failed while attempting to add unmodified file to database: {0}", e.Path);
                        }
                        await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Path, Length = filestatsize, Type = EventType.FileSkipped });
                    }
                }
            });
        }
    }
}

