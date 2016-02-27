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
using Duplicati.Library.Main.Volumes;
using System.Threading.Tasks;
using System.Linq;
using Duplicati.Library.Main.Operation.Common;
using System.IO;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This class encapsulates generation of synthetic file list
    /// </summary>
    internal static class UploadSyntheticFilelist
    {
        public static Task Run(BackupDatabase database, Options options, BackupResults result, ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(new
            {
                LogChannel = Common.Channels.LogChannel.ForWrite,
                UploadChannel = Channels.BackendRequest.ForWrite
            },

            async self => 
            {
                var log = new LogWrapper(self.LogChannel);
                var incompleteFilesets = await database.GetIncompleteFilesetsAsync();
                if (incompleteFilesets.Length != 0)
                {
                    await database.CommitTransactionAsync("PreSyntheticFilelist");

                    result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreviousBackupFinalize);
                    await log.WriteInformationAsync("Uploading filelist from previous interrupted backup");

                    if (!await taskreader.ProgressAsync)
                        return;

                    var incompleteSet = incompleteFilesets.Last();
                    var badIds = from n in incompleteFilesets select n.Key;

                    var prevs = (from n in await database.GetFilesetTimesAsync()
                        where 
                        n.Key < incompleteSet.Key
                        &&
                        !badIds.Contains(n.Key)
                        orderby n.Key                                                
                        select n.Key).ToArray();

                    var prevId = prevs.Length == 0 ? -1 : prevs.Last();

                    FilesetVolumeWriter fsw = null;
                    try
                    {
                        var s = 1;
                        var fileTime = incompleteSet.Value + TimeSpan.FromSeconds(s);
                        var oldFilesetID = incompleteSet.Key;

                        // Probe for an unused filename
                        while (s < 60)
                        {
                            var id = await database.GetRemoteVolumeIDAsync(VolumeBase.GenerateFilename(RemoteVolumeType.Files, options, null, fileTime));
                            if (id < 0)
                                break;

                            fileTime = incompleteSet.Value + TimeSpan.FromSeconds(++s);
                        }

                        fsw = new FilesetVolumeWriter(options, fileTime);
                        fsw.VolumeID = await database.RegisterRemoteVolumeAsync(fsw.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);

                        if (!string.IsNullOrEmpty(options.ControlFiles))
                            foreach(var p in options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                                fsw.AddControlFile(p, options.GetCompressionHintFromFilename(p));

                        var newFilesetID = await database.CreateFilesetAsync(fsw.VolumeID, fileTime);
                        await database.LinkFilesetToVolumeAsync(newFilesetID, fsw.VolumeID);
                        await database.AppendFilesFromPreviousSetAsync(null, newFilesetID, prevId, fileTime);

                        await database.WriteFilesetAsync(fsw, newFilesetID);

                        if (!await taskreader.ProgressAsync)
                            return;
                        
                        await database.UpdateRemoteVolumeAsync(fsw.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                        await database.CommitTransactionAsync("CommitUpdateFilelistVolume");
                        await self.UploadChannel.WriteAsync(new FilesetUploadRequest(fsw));
                        fsw = null;
                    }
                    catch
                    {
                        await database.RollbackTransactionAsync();
                        throw;
                    }
                    finally
                    {
                        if (fsw != null)
                            try { fsw.Dispose(); }
                        catch { fsw = null; }
                    }                          
                }

                if (options.IndexfilePolicy != Options.IndexFileStrategy.None)
                {
                    foreach(var blockfile in await database.GetMissingIndexFilesAsync())
                    {
                        if (!await taskreader.ProgressAsync)
                            return;
                        
                        await log.WriteInformationAsync(string.Format("Re-creating missing index file for {0}", blockfile));
                        var w = await Common.IndexVolumeCreator.CreateIndexVolume(blockfile, options, database);

                        if (!await taskreader.ProgressAsync)
                            return;

                        await database.UpdateRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                        await self.UploadChannel.WriteAsync(new IndexVolumeUploadRequest(w));
                    }
                }
            });
        }
    }
}

