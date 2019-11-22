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
        /// <summary>
        /// The tag used for log messages
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(UploadSyntheticFilelist));

        public static Task Run(BackupDatabase database, Options options, BackupResults result, ITaskReader taskreader, string lasttempfilelist, long lasttempfileid)
        {
            return AutomationExtensions.RunTask(new
            {
                UploadChannel = Channels.BackendRequest.ForWrite
            },

            async self => 
            {                
                // Check if we should upload a synthetic filelist
                if (options.DisableSyntheticFilelist || string.IsNullOrWhiteSpace(lasttempfilelist) || lasttempfileid < 0)
                    return;

                // Check that we still need to process this after the cleanup has performed its duties
                var syntbase = await database.GetRemoteVolumeFromIDAsync(lasttempfileid);

                // If we do not have a valid entry, warn and quit
                if (syntbase.Name == null || syntbase.State != RemoteVolumeState.Uploaded)
                {
                    // TODO: If the repair succeeds, this could give a false warning?
                    Logging.Log.WriteWarningMessage(LOGTAG, "MissingTemporaryFilelist", null, "Expected there to be a temporary fileset for synthetic filelist ({0}, {1}), but none was found?", lasttempfileid, lasttempfilelist);
                    return;
                }

                // Files is missing or repaired
                if (syntbase.Name == null || (syntbase.State != RemoteVolumeState.Uploading && syntbase.State != RemoteVolumeState.Temporary))
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "SkippingSyntheticListUpload", "Skipping synthetic upload because temporary fileset appers to be complete: ({0}, {1}, {2})", lasttempfileid, lasttempfilelist, syntbase.State);
                    return;
                }

                // Ready to build and upload the synthetic list
                await database.CommitTransactionAsync("PreSyntheticFilelist");
                var incompleteFilesets = (await database.GetIncompleteFilesetsAsync()).OrderBy(x => x.Value).ToList();

                result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreviousBackupFinalize);
                Logging.Log.WriteInformationMessage(LOGTAG, "PreviousBackupFilelistUpload", "Uploading filelist from previous interrupted backup");

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

                    try
                    {
                        var s = 1;
                        var fileTime = incompleteSet.Value + TimeSpan.FromSeconds(s);

                        // Probe for an unused filename
                        while (s < 60)
                        {
                            var id = await database.GetRemoteVolumeIDAsync(VolumeBase.GenerateFilename(RemoteVolumeType.Files, options, null, fileTime));
                            if (id < 0)
                                break;

                            fileTime = incompleteSet.Value + TimeSpan.FromSeconds(++s);
                        }

                        using (FilesetVolumeWriter fsw = new FilesetVolumeWriter(options, fileTime))
                        {
                            fsw.VolumeID = await database.RegisterRemoteVolumeAsync(fsw.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);

                            if (!string.IsNullOrEmpty(options.ControlFiles))
                                foreach (var p in options.ControlFiles.Split(new char[] {System.IO.Path.PathSeparator}, StringSplitOptions.RemoveEmptyEntries))
                                    fsw.AddControlFile(p, options.GetCompressionHintFromFilename(p));

                            fsw.CreateFilesetFile(false);
                            var newFilesetID = await database.CreateFilesetAsync(fsw.VolumeID, fileTime);
                            await database.LinkFilesetToVolumeAsync(newFilesetID, fsw.VolumeID);
                            await database.AppendFilesFromPreviousSetAsync(null, newFilesetID, prevId, fileTime);

                            await database.WriteFilesetAsync(fsw, newFilesetID);

                            if (!await taskreader.ProgressAsync)
                                return;

                            await database.UpdateRemoteVolumeAsync(fsw.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                            await database.CommitTransactionAsync("CommitUpdateFilelistVolume");
                            await self.UploadChannel.WriteAsync(new FilesetUploadRequest(fsw));
                        }
                    }
                    catch
                    {
                        await database.RollbackTransactionAsync();
                        throw;
                    }
                }
            );
        }
    }
}

