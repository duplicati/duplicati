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

using Duplicati.Library.Main.Database;
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

        /// <summary>
        /// This method is used to generate and upload a synthetic file list, if needed
        /// </summary>
        /// <param name="database">The database to use</param>
        /// <param name="options">The options to use</param>
        /// <param name="result">The backup results</param>
        /// <param name="taskreader">The task reader to use</param>
        /// <param name="backendManager">The backend manager to use</param>
        /// <param name="lastTempFilelist">The last temporary file list volume</param>
        /// <returns></returns>
        public static async Task Run(LocalBackupDatabase database, Options options, BasicResults result, ITaskReader taskreader, IBackendManager backendManager, RemoteVolumeEntry lastTempFilelist)
        {
            // Check if we should upload a synthetic filelist
            if (options.DisableSyntheticFilelist || string.IsNullOrWhiteSpace(lastTempFilelist.Name) || lastTempFilelist.ID <= 0)
                return;

            // Files is missing or repaired
            if (lastTempFilelist.State != RemoteVolumeState.Uploading && lastTempFilelist.State != RemoteVolumeState.Temporary)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "SkippingSyntheticListUpload", "Skipping synthetic upload because temporary fileset appers to be complete: ({0}, {1}, {2})", lastTempFilelist.ID, lastTempFilelist.Name, lastTempFilelist.State);
                return;
            }

            // Ready to build and upload the synthetic list
            database.CommitAndRestartTransaction("PreSyntheticFilelist");
            var incompleteFilesets = database.GetIncompleteFilesets().OrderBy(x => x.Value).ToList();

            if (!incompleteFilesets.Any())
                return;

            if (!await taskreader.ProgressRendevouz().ConfigureAwait(false))
                return;

            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreviousBackupFinalize);
            Logging.Log.WriteInformationMessage(LOGTAG, "PreviousBackupFilelistUpload", "Uploading filelist from previous interrupted backup");

            var incompleteSet = incompleteFilesets.Last();
            var badIds = incompleteFilesets.Select(n => n.Key);

            var prevs = database.FilesetTimes
                .Where(n => n.Key < incompleteSet.Key && !badIds.Contains(n.Key))
                .OrderBy(n => n.Key)
                .Select(n => n.Key)
                .ToArray();

            var prevId = prevs.Length == 0 ? -1 : prevs.Last();

            FilesetVolumeWriter fsw = null;
            try
            {
                var fileTime = FilesetVolumeWriter.ProbeUnusedFilenameName(database, options, incompleteSet.Value);
                fsw = new FilesetVolumeWriter(options, fileTime);
                fsw.VolumeID = database.RegisterRemoteVolume(fsw.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);

                if (!string.IsNullOrEmpty(options.ControlFiles))
                    foreach (var p in options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                        fsw.AddControlFile(p, options.GetCompressionHintFromFilename(p));

                // We declare this to be a partial backup since the synthetic filelist is only created
                // when a backup is interrupted.
                fsw.CreateFilesetFile(false);
                var newFilesetID = database.CreateFileset(fsw.VolumeID, fileTime);
                database.LinkFilesetToVolume(newFilesetID, fsw.VolumeID);
                database.AppendFilesFromPreviousSet(null, newFilesetID, prevId, fileTime);

                database.WriteFileset(fsw, newFilesetID);
                fsw.Close();

                if (!await taskreader.ProgressRendevouz().ConfigureAwait(false))
                    return;

                database.UpdateRemoteVolume(fsw.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                // If the previous filelist was not uploaded, we register it for deletion, as we have created a new synthetic one
                // Because it is registered as "Deleting", it will be removed from remote storage by the cleanup process if it exists
                if (!string.IsNullOrWhiteSpace(lastTempFilelist.Name) && (lastTempFilelist.State == RemoteVolumeState.Uploading || lastTempFilelist.State == RemoteVolumeState.Temporary))
                    database.UpdateRemoteVolume(lastTempFilelist.Name, RemoteVolumeState.Deleting, -1, null);
                database.CommitAndRestartTransaction("CommitUpdateFilelistVolume");

                await backendManager.PutAsync(fsw, null, null, false, taskreader.ProgressToken);
            }
            catch
            {
                fsw?.Dispose();
                throw;
            }
        }
    }
}

