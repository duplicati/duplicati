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
using System.Threading.Tasks;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation.Backup;

internal static class UploadRealFilelist
{
    /// <summary>
    /// The tag used for log messages
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(UploadRealFilelist));

    public static async Task Run(BackupResults result, BackupDatabase db, IBackendManager backendManager, Options options, FilesetVolumeWriter filesetvolume, long filesetid, Common.ITaskReader taskreader)
    {
        // We ignore the stop signal, but not the pause and terminate
        await taskreader.ProgressRendevouz().ConfigureAwait(false);

        // Update the reported source and backend changes
        using (new Logging.Timer(LOGTAG, "UpdateChangeStatistics", "UpdateChangeStatistics"))
            await db.UpdateChangeStatisticsAsync(result);

        var changeCount =
            result.AddedFiles + result.ModifiedFiles + result.DeletedFiles +
            result.AddedFolders + result.ModifiedFolders + result.DeletedFolders +
            result.AddedSymlinks + result.ModifiedSymlinks + result.DeletedSymlinks;

        //Changes in the filelist triggers a filelist upload
        if (options.UploadUnchangedBackups || changeCount > 0)
        {
            using (new Logging.Timer(LOGTAG, "UploadNewFileset", "Uploading a new fileset"))
            {
                if (!string.IsNullOrEmpty(options.ControlFiles))
                    foreach (var p in options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                        filesetvolume.AddControlFile(p, options.GetCompressionHintFromFilename(p));

                // We ignore the stop signal, but not the pause and terminate
                await taskreader.ProgressRendevouz().ConfigureAwait(false);

                await db.WriteFilesetAsync(filesetvolume, filesetid).ConfigureAwait(false);
                filesetvolume.Close();

                // We ignore the stop signal, but not the pause and terminate
                await taskreader.ProgressRendevouz().ConfigureAwait(false);

                await db.UpdateRemoteVolumeAsync(filesetvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null).ConfigureAwait(false);
                await db.CommitTransactionAsync("CommitUpdateRemoteVolume").ConfigureAwait(false);

                await backendManager.PutAsync(filesetvolume, null, null, false, taskreader.ProgressToken).ConfigureAwait(false);
            }
        }
        else
        {
            if (result.TimestampChangedFiles != 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "DetectedTimestampChanges", "Detected timestamp changes, but no data needs to be uploaded, pushing timestamp changes to the latest fileset");
                await db.PushTimestampChangesToPreviousVersionAsync(filesetid).ConfigureAwait(false);
            }

            Logging.Log.WriteVerboseMessage(LOGTAG, "RemovingLeftoverTempFile", "removing temp files, as no data needs to be uploaded");
            await db.RemoveRemoteVolumeAsync(filesetvolume.RemoteFilename);
        }

        await db.CommitTransactionAsync("CommitUpdateRemoteVolume");
    }
}


