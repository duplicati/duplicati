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
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Volumes;
using System.IO;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal static class UploadRealFilelist
    {
        /// <summary>
        /// The tag used for log messages
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(UploadRealFilelist));

        public static Task Run(BackupResults result, BackupDatabase db, Options options, FilesetVolumeWriter filesetvolume, long filesetid, Common.ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(new
            {
                Output = Channels.BackendRequest.ForWrite,
            },

            async self =>
            {
                if (!await taskreader.ProgressAsync)
                    return;
                
                // Update the reported source and backend changes
                using(new Logging.Timer(LOGTAG, "UpdateChangeStatistics", "UpdateChangeStatistics"))
                    await db.UpdateChangeStatisticsAsync(result);

                var changeCount = 
                    result.AddedFiles + result.ModifiedFiles + result.DeletedFiles +
                    result.AddedFolders + result.ModifiedFolders + result.DeletedFolders +
                    result.AddedSymlinks + result.ModifiedSymlinks + result.DeletedSymlinks;

                //Changes in the filelist triggers a filelist upload
                if (options.UploadUnchangedBackups || changeCount > 0)
                {
                    using(new Logging.Timer(LOGTAG, "UploadNewFileset", "Uploading a new fileset"))
                    {
                        if (!string.IsNullOrEmpty(options.ControlFiles))
                            foreach(var p in options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                                filesetvolume.AddControlFile(p, options.GetCompressionHintFromFilename(p));

                        if (!await taskreader.ProgressAsync)
                            return;

                        await db.WriteFilesetAsync(filesetvolume, filesetid);
                        filesetvolume.Close();

                        if (!await taskreader.ProgressAsync)
                            return;
                        
                        await db.UpdateRemoteVolumeAsync(filesetvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                        await db.CommitTransactionAsync("CommitUpdateRemoteVolume");
                        await self.Output.WriteAsync(new FilesetUploadRequest(filesetvolume));
                    }
                }
                else
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingLeftoverTempFile", "removing temp files, as no data needs to be uploaded");
                    await db.RemoveRemoteVolumeAsync(filesetvolume.RemoteFilename);
                }

                await db.CommitTransactionAsync("CommitUpdateRemoteVolume");
            });
        }
    }
}

