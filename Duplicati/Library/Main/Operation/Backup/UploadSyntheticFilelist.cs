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

using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Volumes;
using System;
using System.Linq;
using System.Threading.Tasks;

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

        public static async Task Run(BackupDatabase database, Options options, BackupResults result, ITaskReader taskreader, IBackendManager backendManager, string lastTempFilelist, long lastTempFilesetId)
        {
            // Check if we should upload a synthetic filelist
            if (options.DisableSyntheticFilelist || string.IsNullOrWhiteSpace(lastTempFilelist) || lastTempFilesetId <= 0)
                return;

            // Check that we still need to process this after the cleanup has performed its duties
            var syntbase = await database.GetRemoteVolumeFromFilesetIDAsync(lastTempFilesetId);

            // If we do not have a valid entry, warn and quit
            if (syntbase.Name == null)
            {
                // TODO: If the repair succeeds, this could give a false warning?
                Logging.Log.WriteWarningMessage(LOGTAG, "MissingTemporaryFilelist", null, "Expected there to be a temporary fileset for synthetic filelist ({0}, {1}), but none was found?", lastTempFilesetId, lastTempFilelist);
                return;
            }

            // Files is missing or repaired
            if (syntbase.State != RemoteVolumeState.Uploading && syntbase.State != RemoteVolumeState.Temporary)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "SkippingSyntheticListUpload", "Skipping synthetic upload because temporary fileset appers to be complete: ({0}, {1}, {2})", lastTempFilesetId, lastTempFilelist, syntbase.State);
                return;
            }

            // Ready to build and upload the synthetic list
            await database.CommitTransactionAsync("PreSyntheticFilelist");
            var incompleteFilesets = (await database.GetIncompleteFilesetsAsync()).OrderBy(x => x.Value).ToList();

            if (!incompleteFilesets.Any())
            {
                return;
            }

            if (!await taskreader.ProgressRendevouz().ConfigureAwait(false))
                return;

            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreviousBackupFinalize);
            Logging.Log.WriteInformationMessage(LOGTAG, "PreviousBackupFilelistUpload", "Uploading filelist from previous interrupted backup");

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
                    foreach (var p in options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                        fsw.AddControlFile(p, options.GetCompressionHintFromFilename(p));

                // We declare this to be a partial backup since the synthetic filelist is only created
                // when a backup is interrupted.
                fsw.CreateFilesetFile(false);
                var newFilesetID = await database.CreateFilesetAsync(fsw.VolumeID, fileTime);
                await database.LinkFilesetToVolumeAsync(newFilesetID, fsw.VolumeID);
                await database.AppendFilesFromPreviousSetAsync(null, newFilesetID, prevId, fileTime);

                await database.WriteFilesetAsync(fsw, newFilesetID);
                fsw.Close();

                if (!await taskreader.ProgressRendevouz().ConfigureAwait(false))
                    return;

                await database.UpdateRemoteVolumeAsync(fsw.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                await database.CommitTransactionAsync("CommitUpdateFilelistVolume");

                await backendManager.PutAsync(fsw, null, null, false, taskreader.ProgressToken);
            }
            catch
            {
                await database.RollbackTransactionAsync();
                fsw?.Dispose();
                throw;
            }
        }
    }
}

