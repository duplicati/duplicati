// Copyright (C) 2026, The Duplicati Team
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    /// <summary>
    /// Asks the destination to bring back the remote volumes a restore of the selected files reads,
    /// when they are in archive storage such as Amazon S3 Glacier or the Azure archive tier (#6228).
    /// Bringing them back can take hours, so the command returns once it has asked; running it again
    /// shows the progress, and the restore can be run once every volume is available.
    /// </summary>
    internal class UnfreezeHandler
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<UnfreezeHandler>();

        /// <summary>
        /// The options for the operation
        /// </summary>
        private readonly Options m_options;
        /// <summary>
        /// The results of the operation
        /// </summary>
        private readonly UnfreezeResults m_result;

        /// <summary>
        /// Creates a new handler
        /// </summary>
        /// <param name="options">The options for the operation</param>
        /// <param name="result">The results of the operation</param>
        public UnfreezeHandler(Options options, UnfreezeResults result)
        {
            m_options = options;
            m_result = result;
        }

        /// <summary>
        /// Asks for the remote volumes the restore of the selected files reads to be brought back
        /// </summary>
        /// <param name="backendUrl">The destination</param>
        /// <param name="paths">The paths to restore, as given to the restore command</param>
        /// <param name="filter">The filter to restore with</param>
        /// <returns>An awaitable task</returns>
        public async Task RunAsync(string backendUrl, string[]? paths, IFilter? filter)
        {
            var token = m_result.TaskControl.ProgressToken;
            var (fileCount, fileSize, volumes) = await ListRestoreVolumesHandler.GetRestoreVolumesAsync(m_options, paths, filter, token).ConfigureAwait(false);
            m_result.FileCount = fileCount;
            m_result.FileSize = fileSize;
            if (volumes.Count == 0)
                return;

            using var backend = DynamicLoader.BackendLoader.GetBackend(backendUrl, m_options.RawOptions)
                ?? throw new UserInformationException(string.Format("Backend not supported: {0}", Library.Utility.Utility.GetUrlWithoutCredentials(backendUrl)), "BackendNotSupported");

            if (!backend.TryGetCapability<IArchiveRestoreEnabledBackend>(out var archiveBackend))
                throw new UserInformationException("This destination cannot be asked to bring files back from archive storage. Use the list-restore-volumes command to list the files a restore reads, and bring them back with the tools of the storage provider.", "ArchiveRestoreNotSupported");

            var results = new List<IUnfreezeVolume>(volumes.Count);
            foreach (var volume in volumes)
            {
                token.ThrowIfCancellationRequested();

                var state = await archiveBackend.GetArchiveRestoreStateAsync(volume.Name, token).ConfigureAwait(false);
                var requested = false;
                if (state == ArchiveRestoreState.Archived)
                {
                    if (m_options.Dryrun)
                    {
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldUnfreezeVolume", "Would ask for {0} to be brought back from archive storage", volume.Name);
                    }
                    else
                    {
                        await archiveBackend.RequestArchiveRestoreAsync(volume.Name, token).ConfigureAwait(false);
                        Logging.Log.WriteInformationMessage(LOGTAG, "UnfreezeVolume", "Asked for {0} to be brought back from archive storage", volume.Name);
                        requested = true;
                        state = ArchiveRestoreState.RestoreInProgress;
                    }
                }

                results.Add(new UnfreezeVolume(volume.Name, volume.Size, state, requested));
            }

            m_result.Volumes = results;

            // The volumes are not readable yet, so a restore run now fails; this is reported as a
            // warning, so a script can wait for the command to finish without one
            var waiting = results.Count(x => x.State != ArchiveRestoreState.Available);
            if (waiting > 0)
                Logging.Log.WriteWarningMessage(LOGTAG, "VolumesNotAvailable", null, "{0} of {1} remote volumes cannot be read yet. Run the command again to see the progress, and run the restore once all of them are available.", waiting, results.Count);
        }
    }
}
