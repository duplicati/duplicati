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
using Duplicati.Library.Main.Operation.Common;
using System.Threading.Tasks;
using System.Threading;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class runs a process which opens a file and outputs data blocks for processing
    /// </summary>
    internal static class FileBlockProcessor
    {
        /// <summary>
        /// The tag to use for log messages
        /// </summary>
        private static readonly string FILELOGTAG = Logging.Log.LogTagFromType(typeof(FileBlockProcessor)) + ".FileEntry";

        public static Task Run(Snapshots.ISnapshotService snapshot, Options options, BackupDatabase database, BackupStatsCollector stats, ITaskReader taskreader, CancellationToken token)
        {
            return AutomationExtensions.RunTask(
            new 
            {
                Input = Channels.AcceptedChangedFile.ForRead,
                StreamBlockChannel = Channels.StreamBlock.ForWrite,
            },

            async self =>
            {
                while (await taskreader.ProgressAsync)
                {
                    var e = await self.Input.ReadAsync();

                    try
                    {
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        var hint = options.GetCompressionHintFromFilename(e.Path);
                        var oldHash = e.OldId < 0 ? null : await database.GetFileHashAsync(e.OldId);

                        StreamProcessResult filestreamdata;

                        // Process metadata and actual data in parallel
                        var metatask =
                            Task.Run(async () =>
                            {
                                // If we have determined that metadata has not changed, just grab the ID
                                if (!e.MetadataChanged)
                                {
                                    var res = await database.GetMetadataIDAsync(e.MetaHashAndSize.FileHash, e.MetaHashAndSize.Blob.Length);
                                    if (res.Item1)
                                        return res.Item2;

                                    Logging.Log.WriteWarningMessage(FILELOGTAG, "UnexpectedMetadataLookup", null, "Metadata was reported as not changed, but still requires being added?\nHash: {0}, Length: {1}, ID: {2}, Path: {3}", e.MetaHashAndSize.FileHash, e.MetaHashAndSize.Blob.Length, res.Item2, e.Path);
                                    e.MetadataChanged = true;
                                }

                                return (await MetadataPreProcess.AddMetadataToOutputAsync(e.Path, e.MetaHashAndSize, database, self.StreamBlockChannel)).Item2;
                            });

                        using (var fs = snapshot.OpenRead(e.Path))                            
                            filestreamdata = await StreamBlock.ProcessStream(self.StreamBlockChannel, e.Path, fs, false, hint);

                        await stats.AddOpenedFile(filestreamdata.Streamlength);

                        var metadataid = await metatask;
                        var filekey = filestreamdata.Streamhash;
                        var filesize = filestreamdata.Streamlength;

                        if (oldHash != filekey)
                        {
                            if (oldHash == null)
                                Logging.Log.WriteVerboseMessage(FILELOGTAG, "NewFile", "New file {0}", e.Path);
                            else
                                Logging.Log.WriteVerboseMessage(FILELOGTAG, "ChangedFile", "File has changed {0}", e.Path);
                            
                            if (e.OldId < 0)
                            {
                                await stats.AddAddedFile(filesize);

                                if (options.Dryrun)
                                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "WouldAddNewFile", "Would add new file {0}, size {1}", e.Path, Library.Utility.Utility.FormatSizeString(filesize));
                            }
                            else
                            {
                                await stats.AddModifiedFile(filesize);

                                if (options.Dryrun)
                                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "WouldAddChangedFile", "Would add changed file {0}, size {1}", e.Path, Library.Utility.Utility.FormatSizeString(filesize));
                            }

                            await database.AddFileAsync(e.PathPrefixID, e.Filename, e.LastWrite, filestreamdata.Blocksetid, metadataid);
                        }
                        else if (e.MetadataChanged)
                        {
                            Logging.Log.WriteVerboseMessage(FILELOGTAG, "FileMetadataChanged", "File has only metadata changes {0}", e.Path);
                            await database.AddFileAsync(e.PathPrefixID, e.Filename, e.LastWrite, filestreamdata.Blocksetid, metadataid);
                        }
                        else /*if (e.OldId >= 0)*/
                        {
                            // When we write the file to output, update the last modified time
                            Logging.Log.WriteVerboseMessage(FILELOGTAG, "NoFileChanges", "File has not changed {0}", e.Path);

                            try
                            {
                                await database.AddUnmodifiedAsync(e.OldId, e.LastWrite);
                            }
                            catch (Exception ex)
                            {
                                Logging.Log.WriteWarningMessage(FILELOGTAG, "FailedToAddFile", ex, "Failed while attempting to add unmodified file to database: {0}", e.Path);
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        if (ex.IsRetiredException())
                            return;
                        else
                            Logging.Log.WriteWarningMessage(FILELOGTAG, "PathProcessingFailed", ex, "Failed to process path: {0}", e.Path);
                    }
                }
            });
        }
    }
}

