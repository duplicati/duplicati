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

        public static Task Run(Channels channels, Options options, BackupStatsCollector stats, BackupDatabase database)
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = channels.ProcessedFiles.AsRead(),
                ProgressChannel = channels.ProgressEvents.AsWrite(),
                Output = channels.AcceptedChangedFile.AsWrite()
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
                        filestatsize = e.Entry.Size;
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteExplicitMessage(FILELOGTAG, "FailedToReadSize", ex, "Failed to read size of file: {0}", e.Entry.Path);
                    }

                    await stats.AddExaminedFile(filestatsize);

                    // Stop now if the file is too large
                    var tooLargeFile = SKIPFILESLARGERTHAN != 0 && filestatsize >= 0 && filestatsize > SKIPFILESLARGERTHAN;
                    if (tooLargeFile)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "SkipCheckTooLarge", "Skipped checking file, because the size exceeds limit {0}", e.Entry.Path);
                        await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Entry.Path, Length = filestatsize, Type = EventType.FileSkipped });
                        continue;
                    }

                    // Invalid ID indicates a new file
                    var isNewFile = e.OldId < 0;

                    // If we disable the filetime check, we always assume that the file has changed
                    // Otherwise we check that the timestamps are different or if any of them are empty
                    e.TimestampChanged = DISABLEFILETIMECHECK || e.LastWrite != e.OldModified || e.LastWrite.Ticks == 0 || e.OldModified.Ticks == 0;

                    // Avoid generating a new metadata blob if timestamp has not changed
                    // and we only check for timestamp changes
                    if (CHECKFILETIMEONLY && !e.TimestampChanged && !isNewFile)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "SkipCheckNoTimestampChange", "Skipped checking file, because timestamp was not updated {0}", e.Entry.Path);
                        try
                        {
                            await database.AddUnmodifiedAsync(e.OldId, e.LastWrite);
                        }
                        catch (Exception ex)
                        {
                            if (ex.IsRetiredException())
                                throw;
                            Logging.Log.WriteWarningMessage(FILELOGTAG, "FailedToAddFile", ex, "Failed while attempting to add unmodified file to database: {0}", e.Entry.Path);
                        }
                        await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Entry.Path, Length = filestatsize, Type = EventType.FileSkipped });
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
                    e.MetaHashAndSize = SKIPMETADATA ? EMPTY_METADATA : Utility.WrapMetadata(MetadataGenerator.GenerateMetadata(e.Entry, e.Attributes, options), options);
                    e.MetadataChanged = !SKIPMETADATA && (e.MetaHashAndSize.Blob.Length != e.OldMetaSize || e.MetaHashAndSize.FileHash != e.OldMetaHash);

                    // Check if the file is new, or something indicates a change
                    var filesizeChanged = filestatsize < 0 || e.LastFileSize < 0 || filestatsize != e.LastFileSize;
                    if (isNewFile || e.TimestampChanged || filesizeChanged || e.MetadataChanged)
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "CheckFileForChanges", "Checking file for changes {0}, new: {1}, timestamp changed: {2}, size changed: {3}, metadatachanged: {4}, {5} vs {6}", e.Entry.Path, isNewFile, e.TimestampChanged, filesizeChanged, e.MetadataChanged, e.LastWrite, e.OldModified);
                        await self.Output.WriteAsync(e);
                    }
                    else
                    {
                        Logging.Log.WriteVerboseMessage(FILELOGTAG, "SkipCheckNoMetadataChange", "Skipped checking file, because no metadata was updated {0}", e.Entry.Path);
                        try
                        {
                            await database.AddUnmodifiedAsync(e.OldId, e.LastWrite);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(FILELOGTAG, "FailedToAddFile", ex, "Failed while attempting to add unmodified file to database: {0}", e.Entry.Path);
                        }
                        await self.ProgressChannel.WriteAsync(new ProgressEvent() { Filepath = e.Entry.Path, Length = filestatsize, Type = EventType.FileSkipped });
                    }
                }
            });
        }
    }
}

