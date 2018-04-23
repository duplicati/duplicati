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
using Duplicati.Library.Main.Operation.Common;
using System.Threading.Tasks;
using System.Collections.Generic;
using Duplicati.Library.Utility;
using System.Linq;
using Duplicati.Library.Interface;
using System.IO;

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

        public static Task Run(Snapshots.ISnapshotService snapshot, Options options, BackupDatabase database, BackupStatsCollector stats, ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(
            new 
            {
                Input = Channels.AcceptedChangedFile.ForRead,
                StreamBlockChannel = Channels.StreamBlock.ForWrite,
            },

            async self =>
            {
                var blocksize = options.Blocksize;

                while (await taskreader.ProgressAsync)
                {
                    var e = await self.Input.ReadAsync();
                    var filesize = 0L;

                    try
                    {
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
                                    if (!res.Item1)
                                        return res.Item2;

                                    Logging.Log.WriteWarningMessage(FILELOGTAG, "UnexpextedMetadataLookup", null, "Metadata was reported as not changed, but still requires being added?\nHash: {0}, Length: {1}, ID: {2}", e.MetaHashAndSize.FileHash, e.MetaHashAndSize.Blob.Length, res.Item2);
                                    e.MetadataChanged = true;
                                }

                                return (await MetadataPreProcess.AddMetadataToOutputAsync(e.Path, e.MetaHashAndSize, database, self.StreamBlockChannel)).Item2;
                            });

                        using (var fs = snapshot.OpenRead(e.Path))                            
                            filestreamdata = await StreamBlock.ProcessStream(self.StreamBlockChannel, e.Path, fs, false, hint);

                        await stats.AddOpenedFile(filestreamdata.Streamlength);

                        var metadataid = await metatask;
                        var filekey = filestreamdata.Streamhash;

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
                                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "WoudlAddNewFile", "Would add new file {0}, size {1}", e.Path, Library.Utility.Utility.FormatSizeString(filesize));
                            }
                            else
                            {
                                await stats.AddModifiedFile(filesize);

                                if (options.Dryrun)
                                    Logging.Log.WriteVerboseMessage(FILELOGTAG, "WoudlAddChangedFile", "Would add changed file {0}, size {1}", e.Path, Library.Utility.Utility.FormatSizeString(filesize));
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
                            await database.AddUnmodifiedAsync(e.OldId, e.LastWrite);
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
            }
            );


        }
    }
}

