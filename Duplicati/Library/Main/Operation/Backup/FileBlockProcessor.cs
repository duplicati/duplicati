#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//
#endregion
using System;
using CoCoL;
using Duplicati.Library.Main.Operation.Common;
using System.IO;
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
        /// Windows error 0x80070020 file open by another process
        public const int ERRORCODEFILEBUSY = -2147024864;

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
                        { /* skips warning for busy files if snapshots to 'auto' - so warnings are still displayed for the default snapshot option value (off) */
                           if (
                                options.SnapShotStrategy == Options.OptimizationStrategy.Auto &&
                                ex is System.IO.IOException &&
                                ex.HResult == ERRORCODEFILEBUSY)
                                {
                                    Logging.Log.WriteInformationMessage(FILELOGTAG, "File open", "File is busy (try to use VSS) {0})", e.Path);
                                }
                                else
                                {
                                    Logging.Log.WriteWarningMessage(FILELOGTAG, "PathProcessingFailed", ex, "Failed to process path: {0}", e.Path);
                                }
                       }
                    }
                }
            });
        }
    }
}

